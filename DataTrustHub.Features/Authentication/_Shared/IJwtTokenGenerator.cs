namespace DataTrustHub.Features.Authentication._Shared;

public interface IJwtTokenGenerator
{
    string Generate(Guid userId, string email);
}
