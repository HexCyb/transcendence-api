using System.Text.RegularExpressions;

namespace Transcendence.Api.Auth;

public static partial class CredentialRules
{
    public const int MinPassword = 10;
    public const int MaxPassword = 128;

    // \A and \z, not ^ and $: "$" would also accept a name that ends with a newline.
    [GeneratedRegex(@"\A[A-Za-z0-9_]{3,16}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    /// <summary>Field name -> messages, in the shape ValidationProblem expects. Empty when everything is valid.</summary>
    public static Dictionary<string, string[]> Validate(string? username, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        if (username is null || !UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["3 to 16 characters: letters, digits or _"];
        }
        if (password is null || password.Length < MinPassword || password.Length > MaxPassword)
        {
            errors["password"] = [$"{MinPassword} to {MaxPassword} characters"];
        }
        else if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            errors["password"] = ["at least one letter and one digit"];
        }
        return errors;
    }
}
