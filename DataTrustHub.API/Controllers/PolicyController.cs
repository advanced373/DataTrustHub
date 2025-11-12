using DataTrustHub.API.Policy.DTOs;
using DataTrustHub.Application.Policy.Create;
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
    }
}

