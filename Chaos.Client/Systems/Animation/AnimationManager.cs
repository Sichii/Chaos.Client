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
    private const int DEFAULT_WALK_FRAMES = 4;
    private const float DEFAULT_WALK_FRAME_MS = 114;
    private const float REMOTE_AISLING_WALK_FRAME_MS = 100f;
    private const float CREATURE_WALK_FRAME_MS = 100f;
    private const float MIN_WALK_DURATION_MS = 75f;

    // The server's AnimationSpeed value × 10 = total animation duration in ms.
    // Smaller speed = faster animation (e.g. speed=25 → 250ms, speed=100 → 1000ms).
    private const float BODY_ANIM_SPEED_TO_MS = 10f;
    private const float DEFAULT_BODY_ANIM_FRAME_MS = 150f;
    private const float CREATURE_ATTACK_FRAME_MS = 300f;
    private const float IDLE_ANIM_FRAME_MS = 250f;

    public const string PEASANT_ANIM_SUFFIX = "03";

    // Aisling walk anim "01": frame 0 = Up idle, 1-4 = Up walk; frame 5 = Right idle, 6-9 = Right walk
    private const int AISLING_UP_WALK_BASE = 1;
    private const int AISLING_RIGHT_WALK_BASE = 6;

    #region Start
    /// <summary>
    ///     Sets up walk animation state on an entity. Call after updating tile position.
    /// </summary>
    public static void StartWalk(
        WorldEntity entity,
        Direction direction,
        bool isCreature = false,
        bool isLocalPlayer = false,
        int? walkFrameOverride = null)
    {
        var frameCount = walkFrameOverride ?? DEFAULT_WALK_FRAMES;

        entity.AnimState = EntityAnimState.Walking;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = frameCount;

        entity.AnimFrameIntervalMs = isCreature
            ? CREATURE_WALK_FRAME_MS
            : isLocalPlayer
                ? DEFAULT_WALK_FRAME_MS
                : REMOTE_AISLING_WALK_FRAME_MS;

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

        (_, var framesPerDir, _, _) = ResolveBodyAnimParams(bodyAnim);

        if (framesPerDir == 0)
            return;

        var repeats = bodyAnim switch
        {
            BodyAnimation.Wave => 1,
            _                  => 0
        };

        float frameIntervalMs;

        // Emotes with body animations use total duration from the emote table, divided across frames and repeats
        if (DataUtilities.IsEmote(bodyAnim))
        {
            (_, _, var emoteDuration) = ResolveEmoteFrames(bodyAnim);
            frameIntervalMs = (emoteDuration > 0 ? emoteDuration : DEFAULT_BODY_ANIM_FRAME_MS) / framesPerDir / (1 + repeats);
        } else

            // Non-emote body animations: animSpeed * 10 = per-frame interval
            frameIntervalMs = animSpeed > 0 ? animSpeed * BODY_ANIM_SPEED_TO_MS : DEFAULT_BODY_ANIM_FRAME_MS;

        entity.AnimState = EntityAnimState.BodyAnim;
        entity.ActiveBodyAnimation = bodyAnim;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = framesPerDir;
        entity.AnimFrameIntervalMs = frameIntervalMs;
        entity.BodyAnimRepeatsLeft = repeats;
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

        if (DataUtilities.IsEmote(bodyAnim))
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
        // Idle animation ticks independently so it survives body animations
        if (entity.IdleAnimFrameCount > 0)
            AdvanceIdleAnim(entity, elapsedMs);

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

        // Total duration includes all frames — each frame gets a full interval (including the last).
        var totalDuration = Math.Max(MIN_WALK_DURATION_MS, entity.AnimFrameCount * entity.AnimFrameIntervalMs);
        var progress = Math.Clamp(entity.AnimElapsedMs / totalDuration, 0f, 1f);

        entity.AnimFrameIndex = Math.Clamp((int)(progress * entity.AnimFrameCount), 0, entity.AnimFrameCount - 1);

        // Both smooth and stepped use integer-only offsets to prevent sub-pixel wobble.
        // The walk start offsets are always integer (±28, ±14), and integer division
        // ensures every intermediate value is also integer.
        if (smoothScroll)
        {
            // Smooth: interpolate at 2x the stepped frame rate (double the visual steps).
            var smoothFrameCount = entity.AnimFrameCount * 2;
            var smoothFrameIndex = Math.Clamp((int)(progress * smoothFrameCount), 0, smoothFrameCount - 1);
            entity.VisualOffset = GetSteppedWalkOffset(entity.WalkStartOffset, smoothFrameIndex, smoothFrameCount);
        } else

            // Stepped: offset jumps discretely with each animation frame
            entity.VisualOffset = GetSteppedWalkOffset(entity.WalkStartOffset, entity.AnimFrameIndex, entity.AnimFrameCount);

        if (progress >= 1f)
            ResetToIdle(entity);
    }

    private static void AdvanceIdleAnim(WorldEntity entity, float elapsedMs)
    {
        entity.IdleAnimElapsedMs += elapsedMs;

        while (entity.IdleAnimElapsedMs >= IDLE_ANIM_FRAME_MS)
        {
            entity.IdleAnimTick++;
            entity.IdleAnimElapsedMs -= IDLE_ANIM_FRAME_MS;
        }
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
        {
            if (entity.BodyAnimRepeatsLeft > 0)
            {
                entity.BodyAnimRepeatsLeft--;
                entity.AnimFrameIndex = 0;
            } else
                ResetToIdle(entity);
        }
    }

    public static void ResetToIdle(WorldEntity entity)
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
    ///     Returns the correct aisling frame index, flip flag, EPF animation suffix, and front-facing flag for the entity's
    ///     current state. IsFrontFacing determines layer draw order (front vs back).
    /// </summary>
    public static (int FrameIndex, bool Flip, string AnimSuffix, bool IsFrontFacing) GetAislingFrame(WorldEntity entity)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
            {
                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var baseFrame = isFront ? AISLING_RIGHT_WALK_BASE : AISLING_UP_WALK_BASE;
                var frameIndex = baseFrame + Math.Clamp(entity.AnimFrameIndex, 0, DEFAULT_WALK_FRAMES - 1);
                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (frameIndex, flip, "01", isFront);
            }

            case EntityAnimState.BodyAnim:
            {
                (var suffix, var framesPerDir, var upStart, var rightStart)
                    = ResolveBodyAnimParams(entity.ActiveBodyAnimation ?? BodyAnimation.Assail);

                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var frameIndex = Math.Clamp(entity.AnimFrameIndex, 0, Math.Max(framesPerDir - 1, 0));
                var dirBase = isFront ? rightStart : upStart;
                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (dirBase + frameIndex, flip, suffix, isFront);
            }

            default:
            {
                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var flip = entity.Direction is Direction.Down or Direction.Left;

                if (entity.IdleAnimFrameCount > 0)
                    return (entity.IdleAnimTick, flip, "04", isFront);

                return (isFront ? 5 : 0, flip, "01", isFront);
            }
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    ///     Maps a BodyAnimation enum to its EPF animation suffix, frames-per-direction count, and the starting frame index
    ///     for Up and Right directions within that EPF file. Multiple animations can share a single suffix file.
    ///     Returns ("01", 0, 0, 0) for emotes (no body animation change).
    /// </summary>
    public static (string Suffix, int FramesPerDirection, int UpStart, int RightStart) ResolveBodyAnimParams(BodyAnimation anim)
    {
        if (DataUtilities.IsEmote(anim))
        {
            // BlowKiss and Wave are emotes with body animations from "03"
            return anim switch
            {
                BodyAnimation.BlowKiss => ("03", 2, 2, 4),
                BodyAnimation.Wave     => ("03", 2, 6, 8),
                _                      => ("01", 0, 0, 0)
            };
        }

        // Reference: ChaosAssetManager AnimationDefinitions
        return anim switch
        {
            // 02 — assail (2 frames per dir)
            BodyAnimation.Assail => ("02", 2, 0, 2),

            // 03 — peasant animations (shared file)
            BodyAnimation.HandsUp => ("03", 1, 0, 1),

            // b — priest/bard
            BodyAnimation.PriestCast => ("b", 3, 0, 3),
            BodyAnimation.PlayNotes  => ("b", 3, 6, 9),

            // c — warrior
            BodyAnimation.TwoHandAtk => ("c", 4, 0, 4),
            BodyAnimation.JumpAttack => ("c", 3, 8, 11),
            BodyAnimation.Swipe      => ("c", 2, 14, 16),
            BodyAnimation.HeavySwipe => ("c", 3, 18, 21),
            BodyAnimation.Jump       => ("c", 3, 24, 27),

            // d — monk
            BodyAnimation.Kick           => ("d", 3, 0, 3),
            BodyAnimation.Punch          => ("d", 2, 6, 8),
            BodyAnimation.RoundHouseKick => ("d", 4, 10, 14),

            // e — rogue
            BodyAnimation.Stab         => ("e", 2, 0, 2),
            BodyAnimation.DoubleStab   => ("e", 2, 4, 6),
            BodyAnimation.BowShot      => ("e", 4, 8, 12),
            BodyAnimation.HeavyBowShot => ("e", 6, 16, 22),
            BodyAnimation.LongBowShot  => ("e", 4, 28, 32),

            // f — wizard
            BodyAnimation.WizardCast => ("f", 2, 0, 2),
            BodyAnimation.Summon     => ("f", 4, 4, 8),

            // HandsUp2 — same as HandsUp
            BodyAnimation.HandsUp2 => ("03", 1, 0, 1),

            // Default fallback — assail
            _ => ("02", 2, 0, 2)
        };
    }

    private const float DEFAULT_EMOTE_DURATION_MS = 1500f;

    /// <summary>
    ///     Maps an emote BodyAnimation to the starting frame index, frame count, and total display duration in emot01.epf.
    ///     Multi-frame emotes cycle evenly across the display duration. Returns (-1, 0, 0) for non-emotes.
    /// </summary>
    public static (int StartFrame, int FrameCount, float DurationMs) ResolveEmoteFrames(BodyAnimation anim)
        => anim switch
        {
            BodyAnimation.Smile       => (0, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Cry         => (1, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Frown       => (2, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Wink        => (3, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Surprise    => (4, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Tongue      => (5, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Pleasant    => (6, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Snore       => (7, 2, 2000),
            BodyAnimation.Mouth       => (9, 2, 2000),
            BodyAnimation.BlowKiss    => (2, 2, 2000),
            BodyAnimation.Wave        => (6, 2, 1375),
            BodyAnimation.RockOn      => (11, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Peace       => (12, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Stop        => (13, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Ouch        => (14, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Impatient   => (15, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Shock       => (18, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Pleasure    => (19, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Love        => (20, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.SweatDrop   => (21, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Whistle     => (22, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Irritation  => (23, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Silly       => (24, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Cute        => (25, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Yelling     => (26, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Mischievous => (27, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Evil        => (28, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Horror      => (29, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.PuppyDog    => (30, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.StoneFaced  => (31, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Tears       => (32, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.FiredUp     => (35, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Confused    => (38, 4, 2000),
            _                         => (-1, 0, 0)
        };

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