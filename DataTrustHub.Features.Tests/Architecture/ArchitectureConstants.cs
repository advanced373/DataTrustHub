using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Tests.Architecture;

internal static class ArchitectureConstants
{
    // NetArchTest matches dependencies on whole namespace segments, so these must not end with a dot
    internal const string NamespaceSeparator = ".";
    internal const string FeaturesRoot = "DataTrustHub.Features";
    internal const string SharedFolder = "_Shared";
    internal const string SharedNamespace = FeaturesRoot + NamespaceSeparator + SharedFolder;

    internal const string ApiNamespace = "DataTrustHub.API";
    internal const string ApplicationNamespace = "DataTrustHub.Application";
    internal const string DomainNamespace = "DataTrustHub.Domain";
    internal const string InfrastructureNamespace = "DataTrustHub.Infrastructure";

    // Commercially licensed libraries the solution must not depend on (ADR-0012)
    internal const string MediatRNamespace = "MediatR";
    internal const string AutoMapperNamespace = "AutoMapper";
    internal const string EntityFrameworkNamespace = "Microsoft.EntityFrameworkCore";

    internal const string HandlerSuffix = "Handler";
    internal const string ValidatorSuffix = "Validator";
    internal const string EndpointSuffix = "Endpoint";

    internal static readonly Assembly FeaturesAssembly = typeof(FeaturesRegistration).Assembly;
    internal static readonly Assembly DomainAssembly = typeof(IPasswordHasher).Assembly;
    internal static readonly Assembly SharedKernelAssembly = typeof(Result).Assembly;
    internal static readonly Assembly InfrastructureAssembly = typeof(DContext).Assembly;

    // ResideInNamespace is a plain string prefix ("Features.Admin" would match "Features.Administration"),
    // so type filters use this pattern: the namespace itself or any of its children
    internal static string NamespaceOrChildren(string ns) => $@"^{Regex.Escape(ns)}(\.|$)";
}
