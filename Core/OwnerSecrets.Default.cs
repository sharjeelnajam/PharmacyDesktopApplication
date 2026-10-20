namespace PharmacyDesktop.Core;

// PLACEHOLDER secrets used only when Core\OwnerSecrets.cs does not exist (e.g. a fresh clone).
// With these values the owner sign-in is disabled. Create your private Core\OwnerSecrets.cs
// (git-ignored) with the same class — see README "Owner setup". Never commit real values.
internal static class OwnerSecrets
{
    public const string OwnerUsername = "owner";
    public const string OwnerPasswordHash = "";
    public const string LicenseSecret = "UExBQ0VIT0xERVItTk9ULUZPUi1QUk9EVUNUSU9OISE=";
}
