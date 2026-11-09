using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Mindflow.Api.Tests.Infrastructure;

public static class Pkce
{
    public static string CreateVerifier() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
