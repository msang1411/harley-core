namespace Identity.Tests.Infrastructure;

public static class TestData
{
    // Satisfies the password policy configured in Identity.Infrastructure.
    public const string Password = "Passw0rd123";

    // Unique per call so tests never collide on the shared database.
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";
}