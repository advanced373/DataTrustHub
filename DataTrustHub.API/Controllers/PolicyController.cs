using DataTrustHub.API.Policy.DTOs;
using DataTrustHub.API.Shared.DTOs;
using DataTrustHub.Application.Policy.Create;
using DataTrustHub.Application.Policy.Get;
using DataTrustHub.SharedKernel;
using System.Linq;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PolicyController : ControllerBase
    {
        private readonly ILogger<PolicyController> _logger;
        private readonly IMediator _mediator;

        public PolicyController(ILogger<PolicyController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        [HttpGet(Name = "GetPolicies")]
        public async Task<IActionResult> GetPolicies()
        {
            var result = await _mediator.Send(new GetPoliciesQuery());

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var response = result.Value.Select(policy => new PolicyResponseDto
            {
                Id = policy.Id,
                Name = policy.Name,
                OrganizationId = policy.OrganizationId,
                ClassificationLevels = policy.ClassificationLevels.Select(level => new ClassificationLevelDto
                {
                    Id = level.Id,
                    Name = level.Name,
                    Priority = level.Priority
                }).ToList()
            });

            return Ok(response);
        }

        [HttpGet("{id:guid}", Name = "GetPolicyById")]
        public async Task<IActionResult> GetPolicyById(Guid id)
        {
            var result = await _mediator.Send(new GetPolicyByIdQuery(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var policy = result.Value;

            return Ok(new PolicyResponseDto
            {
                Id = policy.Id,
                Name = policy.Name,
                OrganizationId = policy.OrganizationId,
                ClassificationLevels = policy.ClassificationLevels.Select(level => new ClassificationLevelDto
                {
                    Id = level.Id,
                    Name = level.Name,
                    Priority = level.Priority
                }).ToList()
            });
        }

        [HttpPost(Name = "CreatePolicy")]
        public async Task<IActionResult> CreatePolicy([FromBody] PolicyDto policyDto)
        {
            var result = await _mediator.Send(new CreatePolicyCommand(policyDto.Name, policyDto.OrganizationId));
            
            if (result.IsFailure)
            {
                return BadRequest(result.Error);
            }

            return Ok(new { Id = result.Value });
        }

        private IActionResult HandleFailure<T>(Result<T> result) => result.Error.Type switch
        {
            ErrorType.NotFound => NotFound(result.Error),
            _ => BadRequest(result.Error)
        };
    }
}

