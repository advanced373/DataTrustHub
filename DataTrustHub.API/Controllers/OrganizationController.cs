using DataTrustHub.API.Organization.DTOs;
using DataTrustHub.Application.Organization.Create;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrganizationController : ControllerBase
    {
        private readonly ILogger<OrganizationController> _logger;
        private readonly IMediator _mediator;

        public OrganizationController(ILogger<OrganizationController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        [HttpPost(Name = "CreateOrganization")]
        public async Task<IActionResult> CreateOrganization([FromBody] OrganizationDto organizationDto)
        {
            var result = await _mediator.Send(new CreateOrganizationCommand(organizationDto.Name));
            
            if (result.IsFailure)
            {
                return BadRequest(result.Error);
            }

            return Ok(new { Id = result.Value });
        }
    }
}

