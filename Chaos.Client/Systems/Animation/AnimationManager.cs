#region
using Chaos.Client.Data.Utilities;
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Systems.Animation;

/// <summary>
///     Static class containing all entity animation logic. WorldEntity is a data bag; this class provides pure methods to
///     start, advance, and resolve animation state for rendering.
/// </summary>
public static class AnimationManager
{
    private const int DEFAULT_WALK_FRAMES = 5;
    private const float DEFAULT_WALK_FRAME_MS = 100f;

    // The server's AnimationSpeed value × 10 = total animation duration in ms.
    // Smaller speed = faster animation (e.g. speed=25 → 250ms, speed=100 → 1000ms).
    private const float BODY_ANIM_SPEED_TO_MS = 10f;
    private const float DEFAULT_BODY_ANIM_FRAME_MS = 150f;
    private const float CREATURE_ATTACK_FRAME_MS = 300f;

    // Aisling walk anim "01": frames 0-4 = Up (back-facing), frames 5-9 = Right (front-facing)
    private const int AISLING_UP_BASE = 0;
    private const int AISLING_RIGHT_BASE = 5;

    #region Start
    /// <summary>
    ///     Sets up walk animation state on an entity. Call after updating tile position.
    /// </summary>
    public static void StartWalk(WorldEntity entity, Direction direction)
    {
        entity.AnimState = EntityAnimState.Walking;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = DEFAULT_WALK_FRAMES;
        entity.AnimFrameIntervalMs = DEFAULT_WALK_FRAME_MS;
        entity.WalkStartOffset = GetWalkOffset(direction);
        entity.VisualOffset = entity.WalkStartOffset;
    }

    /// <summary>
    ///     Sets up a body animation (attack/cast) on an aisling entity. Ignores if currently walking.
    /// </summary>
    public static void StartBodyAnimation(WorldEntity entity, BodyAnimation bodyAnim, ushort animSpeed)
    {
        if (entity.AnimState == EntityAnimState.Walking)
            return;

        (_, var framesPerDir) = ResolveBodyAnimParams(bodyAnim);

        if (framesPerDir == 0)
            return;

        entity.AnimState = EntityAnimState.BodyAnim;
        entity.ActiveBodyAnimation = bodyAnim;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = framesPerDir;
        entity.AnimFrameIntervalMs = animSpeed > 0 ? animSpeed * BODY_ANIM_SPEED_TO_MS : DEFAULT_BODY_ANIM_FRAME_MS;
    }

    /// <summary>
    ///     Sets up an attack animation on a creature entity. Maps BodyAnimation to attack index: Kick(131)→Attack2,
    ///     RoundHouseKick(133)→Attack3, everything else→Attack1. Falls back to Attack1 if the mapped attack has no frames.
    ///     Monster attack frame delay is hardcoded to 300ms in the original client.
    /// </summary>
    public static void StartCreatureBodyAnimation(
        WorldEntity entity,
        BodyAnimation bodyAnim,
        ushort animSpeed,
        in CreatureAnimInfo animInfo)
    {
        if (entity.AnimState == EntityAnimState.Walking)
            return;

        if (Helpers.IsEmote(bodyAnim))
            return;

        (var startIndex, var framesPerDir) = ResolveCreatureAttack(bodyAnim, in animInfo);

        if (framesPerDir == 0)
            return;

        entity.AnimState = EntityAnimState.BodyAnim;
        entity.ActiveBodyAnimation = bodyAnim;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = framesPerDir;
        entity.AnimFrameIntervalMs = CREATURE_ATTACK_FRAME_MS;
    }

    /// <summary>
    ///     Maps a BodyAnimation to the correct creature attack range. Falls back to Attack1 if the mapped attack has no
    ///     frames.
    /// </summary>
    private static (byte StartIndex, byte FrameCount) ResolveCreatureAttack(BodyAnimation bodyAnim, in CreatureAnimInfo info)
    {
        (var startIndex, var frameCount) = bodyAnim switch
        {
            BodyAnimation.Kick when info.Attack2FrameCount > 0           => (info.Attack2StartIndex, info.Attack2FrameCount),
            BodyAnimation.RoundHouseKick when info.Attack3FrameCount > 0 => (info.Attack3StartIndex, info.Attack3FrameCount),
            _                                                            => (info.AttackFrameIndex, info.AttackFrameCount)
        };

        return (startIndex, frameCount);
    }
    #endregion

    #region Advance
    /// <summary>
    ///     Advances an entity's animation by the given elapsed milliseconds. Handles walk offset, frame stepping, and
    ///     completion reset to idle.
    /// </summary>
    /// <param name="smoothScroll">
    ///     When true (smooth), the player's walk visual offset lerps continuously between frames. When false (rough/default),
    ///     the visual offset steps discretely with each animation frame, matching how all non-player entities move. Only
    ///     relevant for the player entity.
    /// </param>
    public static void Advance(WorldEntity entity, float elapsedMs, bool smoothScroll = false)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
                AdvanceWalk(entity, elapsedMs, smoothScroll);

                break;

            case EntityAnimState.BodyAnim:
                AdvanceBodyAnim(entity, elapsedMs);

                break;
        }
    }

    private static void AdvanceWalk(WorldEntity entity, float elapsedMs, bool smoothScroll)
    {
        entity.AnimElapsedMs += elapsedMs;

        var totalDuration = entity.AnimFrameCount * entity.AnimFrameIntervalMs;
        var progress = Math.Clamp(entity.AnimElapsedMs / totalDuration, 0f, 1f);

        entity.AnimFrameIndex = Math.Clamp((int)(progress * entity.AnimFrameCount), 0, entity.AnimFrameCount - 1);

        // Both smooth and stepped use integer-only offsets to prevent sub-pixel wobble.
        // The walk start offsets are always integer (±28, ±14), and integer division
        // ensures every intermediate value is also integer.
        if (smoothScroll)
        {
            // Smooth: interpolate using elapsed time, but snap to integer pixels
            var startX = (int)entity.WalkStartOffset.X;
            var startY = (int)entity.WalkStartOffset.Y;
            var elapsedSteps = (int)(progress * 1000);
            var x = startX - startX * elapsedSteps / 1000;
            var y = startY - startY * elapsedSteps / 1000;
            entity.VisualOffset = new Vector2(x, y);
        } else

            // Stepped: offset jumps discretely with each animation frame
            entity.VisualOffset = GetSteppedWalkOffset(entity.WalkStartOffset, entity.AnimFrameIndex, entity.AnimFrameCount);

        if (progress >= 1f)
            ResetToIdle(entity);
    }

    private static void AdvanceBodyAnim(WorldEntity entity, float elapsedMs)
    {
        entity.AnimElapsedMs += elapsedMs;

        while (entity.AnimElapsedMs >= entity.AnimFrameIntervalMs)
        {
            entity.AnimFrameIndex++;
            entity.AnimElapsedMs -= entity.AnimFrameIntervalMs;
        }

        if (entity.AnimFrameIndex >= entity.AnimFrameCount)
            ResetToIdle(entity);
    }

    private static void ResetToIdle(WorldEntity entity)
    {
        entity.AnimState = EntityAnimState.Idle;
        entity.ActiveBodyAnimation = null;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.VisualOffset = Vector2.Zero;
    }
    #endregion

    #region Frame Resolution
    /// <summary>
    ///     Returns the correct creature sprite frame index and flip flag for the entity's current animation state.
    /// </summary>
    public static (int FrameIndex, bool Flip) GetCreatureFrame(WorldEntity entity, in CreatureAnimInfo info)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
            {
                var framesPerDir = info.WalkFrameCount;

                if (framesPerDir == 0)
                    return GetCreatureIdleFrame(entity.Direction, in info);

                var mappedFrame = MapFrameIndex(entity.AnimFrameIndex, DEFAULT_WALK_FRAMES, framesPerDir);

                return entity.Direction switch
                {
                    Direction.Up    => (info.WalkFrameIndex + mappedFrame, false),
                    Direction.Right => (info.WalkFrameIndex + framesPerDir + mappedFrame, false),
                    Direction.Down  => (info.WalkFrameIndex + framesPerDir + mappedFrame, true),
                    Direction.Left  => (info.WalkFrameIndex + mappedFrame, true),
                    _               => (info.WalkFrameIndex + mappedFrame, false)
                };
            }

            case EntityAnimState.BodyAnim:
            {
                // Use the correct attack range based on the active BodyAnimation
                (var attackStart, var framesPerDir) = ResolveCreatureAttack(entity.ActiveBodyAnimation ?? BodyAnimation.Assail, in info);

                if (framesPerDir == 0)
                    return GetCreatureIdleFrame(entity.Direction, in info);

                var frameIndex = Math.Min(entity.AnimFrameIndex, framesPerDir - 1);

                return entity.Direction switch
                {
                    Direction.Up    => (attackStart + frameIndex, false),
                    Direction.Right => (attackStart + framesPerDir + frameIndex, false),
                    Direction.Down  => (attackStart + framesPerDir + frameIndex, true),
                    Direction.Left  => (attackStart + frameIndex, true),
                    _               => (info.AttackFrameIndex + frameIndex, false)
                };
            }

            default:
                return GetCreatureIdleFrame(entity.Direction, in info);
        }
    }

    /// <summary>
    ///     Returns the correct aisling frame index, flip flag, and EPF animation suffix for the entity's current state.
    /// </summary>
    public static (int FrameIndex, bool Flip, string AnimSuffix) GetAislingFrame(WorldEntity entity)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
            {
                var baseFrame = entity.Direction is Direction.Right or Direction.Down ? AISLING_RIGHT_BASE : AISLING_UP_BASE;

                var frameIndex = baseFrame + Math.Clamp(entity.AnimFrameIndex, 0, DEFAULT_WALK_FRAMES - 1);
                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (frameIndex, flip, "01");
            }

            case EntityAnimState.BodyAnim:
            {
                (var suffix, var framesPerDir) = ResolveBodyAnimParams(entity.ActiveBodyAnimation ?? BodyAnimation.Assail);
                var frameIndex = Math.Clamp(entity.AnimFrameIndex, 0, framesPerDir - 1);

                // Direction base offsets differ by animation suffix
                var dirBase = suffix switch
                {
                    "02" => entity.Direction is Direction.Right or Direction.Down ? 2 : 0,
                    "03" => entity.Direction is Direction.Right or Direction.Down ? 5 : 0,
                    _    => entity.Direction is Direction.Right or Direction.Down ? AISLING_RIGHT_BASE : AISLING_UP_BASE
                };

                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (dirBase + frameIndex, flip, suffix);
            }

            default:
            {
                // Idle
                return entity.Direction switch
                {
                    Direction.Up    => (AISLING_UP_BASE, false, "01"),
                    Direction.Right => (AISLING_RIGHT_BASE, false, "01"),
                    Direction.Down  => (AISLING_RIGHT_BASE, true, "01"),
                    Direction.Left  => (AISLING_UP_BASE, true, "01"),
                    _               => (AISLING_UP_BASE, false, "01")
                };
            }
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    ///     Maps a BodyAnimation enum to its EPF animation suffix and frames-per-direction count. Returns ("01", 0) for emotes
    ///     (no body animation change).
    /// </summary>
    public static (string Suffix, int FramesPerDirection) ResolveBodyAnimParams(BodyAnimation anim)
    {
        if (Helpers.IsEmote(anim))
            return ("01", 0);

        return anim switch
        {
            BodyAnimation.PriestCast or BodyAnimation.WizardCast or BodyAnimation.PlayNotes or BodyAnimation.Summon => ("03", 5),
            _                                                                                                       => ("02", 2)
        };
    }

    private static (int FrameIndex, bool Flip) GetCreatureIdleFrame(Direction direction, in CreatureAnimInfo info)
    {
        int baseIndex;
        int dirOffset;

        if (info.StandingFrameCount > 0)
        {
            baseIndex = info.StandingFrameIndex;

            // Standing direction offset uses OptionalAnimationFrameCount when available
            dirOffset = info.OptionalAnimationFrameCount > 0 ? info.OptionalAnimationFrameCount : info.StandingFrameCount;
        } else
        {
            // Fallback: first walk frame per direction
            baseIndex = info.WalkFrameIndex;
            dirOffset = info.WalkFrameCount;
        }

        return direction switch
        {
            Direction.Up    => (baseIndex, false),
            Direction.Right => (baseIndex + dirOffset, false),
            Direction.Down  => (baseIndex + dirOffset, true),
            Direction.Left  => (baseIndex, true),
            _               => (baseIndex, false)
        };
    }

    /// <summary>
    ///     Computes the starting visual offset for a walk animation. The entity has already moved to the new tile; this offset
    ///     places it visually at the old position, then lerps to zero.
    /// </summary>
    private static Vector2 GetWalkOffset(Direction direction)
        => direction switch
        {
            Direction.Up    => new Vector2(-28, 14),
            Direction.Right => new Vector2(-28, -14),
            Direction.Down  => new Vector2(28, -14),
            Direction.Left  => new Vector2(28, 14),
            _               => Vector2.Zero
        };

    /// <summary>
    ///     Returns a pixel-snapped walk offset for the given frame. Uses integer division to distribute the total pixel delta
    ///     evenly across frames, avoiding fractional positions that cause wobble on the isometric grid.
    /// </summary>
    private static Vector2 GetSteppedWalkOffset(Vector2 startOffset, int frameIndex, int frameCount)
    {
        if (frameCount <= 0)
            return Vector2.Zero;

        // Compute remaining offset, ensuring X is always even so that Y = X/2 is exact.
        // This maintains the 2:1 isometric pixel ratio and prevents 1px wobble.
        var startX = (int)startOffset.X;
        var startY = (int)startOffset.Y;
        var framesLeft = frameCount - (frameIndex + 1);

        var x = startX * framesLeft / frameCount;

        // Round X to nearest even number (toward zero) to keep the 2:1 ratio exact
        if ((x & 1) != 0)
            x += x > 0 ? -1 : 1;

        var y = x * startY / startX;

        return new Vector2(x, y);
    }

    /// <summary>
    ///     Maps a logical frame index (0..logicalCount-1) to an actual frame index (0..actualCount-1) proportionally. Used
    ///     when the walk animation has a different frame count than the default 5.
    /// </summary>
    private static int MapFrameIndex(int logicalFrame, int logicalCount, int actualCount)
    {
        if ((logicalCount <= 0) || (actualCount <= 0))
            return 0;

        return Math.Clamp(logicalFrame * actualCount / logicalCount, 0, actualCount - 1);
    }
    #endregion
}