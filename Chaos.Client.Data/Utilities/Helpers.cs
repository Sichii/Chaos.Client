using Chaos.Client.Data.Definitions;
using Chaos.Common.Definitions;

namespace Chaos.Client.Data.Utilities;

public static class Helpers
{
    public static string DetermineArchiveSuffix(VisibleObjectType visibleObjectType)
        => visibleObjectType switch
        {
            VisibleObjectType.Body      => "b",
            VisibleObjectType.Face      => "f",
            VisibleObjectType.Weapon    => "w",
            VisibleObjectType.Armor     => "a",
            VisibleObjectType.Armor2    => "a2",
            VisibleObjectType.Shield    => "s",
            VisibleObjectType.Helmet    => "h",
            VisibleObjectType.Boots     => "b",
            VisibleObjectType.Accessory => "a",
            _                           => throw new ArgumentOutOfRangeException(nameof(visibleObjectType), visibleObjectType, null)
        };

    public static BaseClass DetermineClass(BodyAnimation bodyAnimation)
        => bodyAnimation switch
        {
            BodyAnimation.None           => BaseClass.Peasant,
            BodyAnimation.Assail         => BaseClass.Peasant,
            BodyAnimation.HandsUp        => BaseClass.Peasant,
            BodyAnimation.Smile          => BaseClass.Peasant,
            BodyAnimation.Cry            => BaseClass.Peasant,
            BodyAnimation.Frown          => BaseClass.Peasant,
            BodyAnimation.Wink           => BaseClass.Peasant,
            BodyAnimation.Surprise       => BaseClass.Peasant,
            BodyAnimation.Tongue         => BaseClass.Peasant,
            BodyAnimation.Pleasant       => BaseClass.Peasant,
            BodyAnimation.Snore          => BaseClass.Peasant,
            BodyAnimation.Mouth          => BaseClass.Peasant,
            BodyAnimation.BlowKiss       => BaseClass.Peasant,
            BodyAnimation.Wave           => BaseClass.Peasant,
            BodyAnimation.RockOn         => BaseClass.Peasant,
            BodyAnimation.Peace          => BaseClass.Peasant,
            BodyAnimation.Stop           => BaseClass.Peasant,
            BodyAnimation.Ouch           => BaseClass.Peasant,
            BodyAnimation.Impatient      => BaseClass.Peasant,
            BodyAnimation.Shock          => BaseClass.Peasant,
            BodyAnimation.Pleasure       => BaseClass.Peasant,
            BodyAnimation.Love           => BaseClass.Peasant,
            BodyAnimation.SweatDrop      => BaseClass.Peasant,
            BodyAnimation.Whistle        => BaseClass.Peasant,
            BodyAnimation.Irritation     => BaseClass.Peasant,
            BodyAnimation.Silly          => BaseClass.Peasant,
            BodyAnimation.Cute           => BaseClass.Peasant,
            BodyAnimation.Yelling        => BaseClass.Peasant,
            BodyAnimation.Mischievous    => BaseClass.Peasant,
            BodyAnimation.Evil           => BaseClass.Peasant,
            BodyAnimation.Horror         => BaseClass.Peasant,
            BodyAnimation.PuppyDog       => BaseClass.Peasant,
            BodyAnimation.StoneFaced     => BaseClass.Peasant,
            BodyAnimation.Tears          => BaseClass.Peasant,
            BodyAnimation.FiredUp        => BaseClass.Peasant,
            BodyAnimation.Confused       => BaseClass.Peasant,
            BodyAnimation.PriestCast     => BaseClass.Priest,
            BodyAnimation.TwoHandAtk     => BaseClass.Warrior,
            BodyAnimation.Jump           => BaseClass.Warrior,
            BodyAnimation.Kick           => BaseClass.Monk,
            BodyAnimation.Punch          => BaseClass.Monk,
            BodyAnimation.RoundHouseKick => BaseClass.Monk,
            BodyAnimation.Stab           => BaseClass.Rogue,
            BodyAnimation.DoubleStab     => BaseClass.Rogue,
            BodyAnimation.WizardCast     => BaseClass.Wizard,
            BodyAnimation.PlayNotes      => BaseClass.Priest,
            BodyAnimation.HandsUp2       => BaseClass.Peasant,
            BodyAnimation.Swipe          => BaseClass.Warrior,
            BodyAnimation.HeavySwipe     => BaseClass.Warrior,
            BodyAnimation.JumpAttack     => BaseClass.Warrior,
            BodyAnimation.BowShot        => BaseClass.Rogue,
            BodyAnimation.HeavyBowShot   => BaseClass.Rogue,
            BodyAnimation.LongBowShot    => BaseClass.Rogue,
            BodyAnimation.Summon         => BaseClass.Wizard,
            _                            => throw new ArgumentOutOfRangeException(nameof(bodyAnimation), bodyAnimation, null)
        };

    public static char DetermineClassSuffix(BaseClass baseClass)
        => baseClass switch
        {
            BaseClass.Warrior => 'c',
            BaseClass.Rogue   => 'e',
            BaseClass.Wizard  => 'f',
            BaseClass.Priest  => 'b',
            BaseClass.Monk    => 'd',
            _                 => '0'
        };

    public static Gender DetermineGender(BodySprite bodySprite)
        => bodySprite switch
        {
            BodySprite.None        => 0,
            BodySprite.Male        => Gender.Male,
            BodySprite.Female      => Gender.Female,
            BodySprite.MaleGhost   => Gender.Male,
            BodySprite.FemaleGhost => Gender.Female,
            BodySprite.MaleInvis   => Gender.Male,
            BodySprite.FemaleInvis => Gender.Female,
            BodySprite.MaleJester  => Gender.Male,
            BodySprite.MaleHead    => Gender.Male,
            BodySprite.FemaleHead  => Gender.Female,
            BodySprite.BlankMale   => Gender.Male,
            BodySprite.BlankFemale => Gender.Female,
            _                      => 0
        };

    public static (string ArchiveName, string EntryName) DetermineKhanDetails(BodyAnimation bodyAnimation, BodySprite bodySprite)
    {
        var gender = DetermineGender(bodySprite);
        var baseClass = DetermineClass(bodyAnimation);
        var classSuffix = DetermineClassSuffix(baseClass);
        var isEmote = IsEmote(bodyAnimation);

        var archiveName = "khan";
        var fileName = string.Empty;

        if (gender == Gender.Male)
        {
            archiveName += "m";
            fileName += "m";
        } else
        {
            archiveName += "w";
            fileName += "w";
        }

        return default;
    }

    public static bool IsEmote(BodyAnimation bodyAnimation)
        => bodyAnimation switch
        {
            BodyAnimation.None           => false,
            BodyAnimation.Assail         => false,
            BodyAnimation.HandsUp        => true,
            BodyAnimation.Smile          => true,
            BodyAnimation.Cry            => true,
            BodyAnimation.Frown          => true,
            BodyAnimation.Wink           => true,
            BodyAnimation.Surprise       => true,
            BodyAnimation.Tongue         => true,
            BodyAnimation.Pleasant       => true,
            BodyAnimation.Snore          => true,
            BodyAnimation.Mouth          => true,
            BodyAnimation.BlowKiss       => true,
            BodyAnimation.Wave           => true,
            BodyAnimation.RockOn         => true,
            BodyAnimation.Peace          => true,
            BodyAnimation.Stop           => true,
            BodyAnimation.Ouch           => true,
            BodyAnimation.Impatient      => true,
            BodyAnimation.Shock          => true,
            BodyAnimation.Pleasure       => true,
            BodyAnimation.Love           => true,
            BodyAnimation.SweatDrop      => true,
            BodyAnimation.Whistle        => true,
            BodyAnimation.Irritation     => true,
            BodyAnimation.Silly          => true,
            BodyAnimation.Cute           => true,
            BodyAnimation.Yelling        => true,
            BodyAnimation.Mischievous    => true,
            BodyAnimation.Evil           => true,
            BodyAnimation.Horror         => true,
            BodyAnimation.PuppyDog       => true,
            BodyAnimation.StoneFaced     => true,
            BodyAnimation.Tears          => true,
            BodyAnimation.FiredUp        => true,
            BodyAnimation.Confused       => true,
            BodyAnimation.PriestCast     => false,
            BodyAnimation.TwoHandAtk     => false,
            BodyAnimation.Jump           => false,
            BodyAnimation.Kick           => false,
            BodyAnimation.Punch          => false,
            BodyAnimation.RoundHouseKick => false,
            BodyAnimation.Stab           => false,
            BodyAnimation.DoubleStab     => false,
            BodyAnimation.WizardCast     => false,
            BodyAnimation.PlayNotes      => false,
            BodyAnimation.HandsUp2       => false,
            BodyAnimation.Swipe          => false,
            BodyAnimation.HeavySwipe     => false,
            BodyAnimation.JumpAttack     => false,
            BodyAnimation.BowShot        => false,
            BodyAnimation.HeavyBowShot   => false,
            BodyAnimation.LongBowShot    => false,
            BodyAnimation.Summon         => false,
            _                            => false
        };
}