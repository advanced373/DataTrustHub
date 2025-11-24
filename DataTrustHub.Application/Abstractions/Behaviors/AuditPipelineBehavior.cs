using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Audit;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Reflection;

namespace DataTrustHub.Application.Abstractions.Behaviors
{
    public class AuditPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IBaseCommand
    {
        private readonly IAuditRepository _auditRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditPipelineBehavior(
            IAuditRepository auditRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _auditRepository = auditRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            // Execute the command first
            var response = await next();

            // Only audit Create and Delete commands
            var commandName = request.GetType().Name;
            if (!IsCreateOrDeleteCommand(commandName))
            {
                return response;
            }

            try
            {
                var auditInfo = ExtractAuditInfo(request, commandName, response);
                if (auditInfo != null)
                {
                    var auditLog = new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        Action = auditInfo.Action,
                        EntityType = auditInfo.EntityType,
                        EntityId = auditInfo.EntityId,
                        Timestamp = DateTime.UtcNow,
                        UserId = GetUserId(),
                        IpAddress = GetIpAddress(),
                        UserAgent = GetUserAgent(),
                        RequestPath = GetRequestPath(),
                        RequestMethod = GetRequestMethod()
                    };

                    await _auditRepository.AddAsync(auditLog);
                }
            }
            catch (Exception)
            {
                // Don't fail the request if audit logging fails
                // In production, you might want to log this error
            }

            return response;
        }

        private bool IsCreateOrDeleteCommand(string commandName)
        {
            return commandName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                   commandName.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                   commandName.Contains("Register", StringComparison.OrdinalIgnoreCase);
        }

        private AuditInfo? ExtractAuditInfo(TRequest request, string commandName, TResponse response)
        {
            var action = DetermineAction(commandName);
            if (action == null) return null;

            var entityType = ExtractEntityType(commandName);
            if (entityType == null) return null;

            var entityId = ExtractEntityId(request, commandName, action, response);
            if (entityId == null) return null;

            return new AuditInfo
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId.Value
            };
        }

        private string? DetermineAction(string commandName)
        {
            if (commandName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                commandName.Contains("Register", StringComparison.OrdinalIgnoreCase))
            {
                return "Create";
            }
            if (commandName.Contains("Delete", StringComparison.OrdinalIgnoreCase))
            {
                return "Delete";
            }
            return null;
        }

        private string? ExtractEntityType(string commandName)
        {
            // Remove common prefixes/suffixes
            var cleaned = commandName
                .Replace("Create", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Delete", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Register", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Command", "", StringComparison.OrdinalIgnoreCase)
                .Replace("ForUser", "", StringComparison.OrdinalIgnoreCase);

            // Handle special cases
            if (cleaned.Contains("User", StringComparison.OrdinalIgnoreCase))
                return "User";
            if (cleaned.Contains("Organization", StringComparison.OrdinalIgnoreCase))
                return "Organization";
            if (cleaned.Contains("Policy", StringComparison.OrdinalIgnoreCase))
                return "Policy";
            if (cleaned.Contains("DataItem", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("Data", StringComparison.OrdinalIgnoreCase))
                return "DataItem";
            if (cleaned.Contains("Clearance", StringComparison.OrdinalIgnoreCase))
                return "Clearance";

            return cleaned;
        }

        private Guid? ExtractEntityId(TRequest request, string commandName, string action, TResponse response)
        {
            var requestType = request.GetType();
            var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            if (action == "Delete")
            {
                // For delete commands, look for properties like UserId, OrganizationId, etc.
                foreach (var prop in properties)
                {
                    if (prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                        prop.PropertyType == typeof(Guid))
                    {
                        var value = prop.GetValue(request);
                        if (value is Guid guid)
                        {
                            return guid;
                        }
                    }
                }
            }
            else if (action == "Create")
            {
                // For create commands, try to extract ID from the response
                // Response is typically Result<Guid> or Result<T>
                if (response != null)
                {
                    var responseType = response.GetType();
                    
                    // Check if it's a Result<Guid> type
                    if (responseType.IsGenericType)
                    {
                        var genericTypeDefinition = responseType.GetGenericTypeDefinition();
                        var genericArgs = responseType.GetGenericArguments();
                        
                        // Check if it's Result<T> (from DataTrustHub.SharedKernel namespace)
                        var resultType = typeof(Result<>);
                        if (genericTypeDefinition == resultType && genericArgs.Length > 0 && genericArgs[0] == typeof(Guid))
                        {
                            // Try to get the Value property
                            var valueProperty = responseType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                            if (valueProperty != null)
                            {
                                var isSuccessProperty = responseType.GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
                                if (isSuccessProperty != null)
                                {
                                    var isSuccess = (bool)(isSuccessProperty.GetValue(response) ?? false);
                                    if (isSuccess)
                                    {
                                        try
                                        {
                                            var value = valueProperty.GetValue(response);
                                            if (value is Guid guid)
                                            {
                                                return guid;
                                            }
                                        }
                                        catch
                                        {
                                            // Value property might throw if IsSuccess is false, but we already checked
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Fallback: check if there's an ID property in the command
                foreach (var prop in properties)
                {
                    if (prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                        prop.PropertyType == typeof(Guid))
                    {
                        var value = prop.GetValue(request);
                        if (value is Guid guid && guid != Guid.Empty)
                        {
                            return guid;
                        }
                    }
                }
            }

            return null;
        }

        private string? GetUserId()
        {
            // If authentication is implemented, get user ID from claims
            // For now, return null
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        private string? GetIpAddress()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            // Try to get the real IP address (considering proxies)
            var ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
            }
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            }

            return ipAddress;
        }

        private string? GetUserAgent()
        {
            return _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString();
        }

        private string? GetRequestPath()
        {
            return _httpContextAccessor.HttpContext?.Request.Path.ToString();
        }

        private string? GetRequestMethod()
        {
            return _httpContextAccessor.HttpContext?.Request.Method;
        }

        private class AuditInfo
        {
            public string Action { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public Guid EntityId { get; set; }
        }
    }
}

