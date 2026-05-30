namespace DataTrustHub.Features._Shared.Jwt;

public interface IJwtTokenGenerator
{
    string Generate(Guid userId, string email);
}
