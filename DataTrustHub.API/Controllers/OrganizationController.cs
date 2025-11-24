using DataTrustHub.API.Organization.DTOs;
using DataTrustHub.Application.Organization.Create;
using DataTrustHub.Application.Organization.Delete;
using DataTrustHub.Application.Organization.Get;
using DataTrustHub.SharedKernel;
using System.Linq;
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

        [HttpGet(Name = "GetOrganizations")]
        public async Task<IActionResult> GetOrganizations()
        {
            var result = await _mediator.Send(new GetOrganizationsQuery());

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var response = result.Value.Select(organization => new OrganizationResponseDto
            {
                Id = organization.Id,
                Name = organization.Name
            });

            return Ok(response);
        }

        [HttpGet("{id:guid}", Name = "GetOrganizationById")]
        public async Task<IActionResult> GetOrganizationById(Guid id)
        {
            var result = await _mediator.Send(new GetOrganizationByIdQuery(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var organization = result.Value;

            return Ok(new OrganizationResponseDto
            {
                Id = organization.Id,
                Name = organization.Name
            });
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

        [HttpDelete("{id:guid}", Name = "DeleteOrganization")]
        public async Task<IActionResult> DeleteOrganization(Guid id)
        {
            var result = await _mediator.Send(new DeleteOrganizationCommand(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            return NoContent();
        }

        private IActionResult HandleFailure<T>(Result<T> result) => result.Error.Type switch
        {
            ErrorType.NotFound => NotFound(result.Error),
            _ => BadRequest(result.Error)
        };

        private IActionResult HandleFailure(Result result) => result.Error.Type switch
        {
            ErrorType.NotFound => NotFound(result.Error),
            _ => BadRequest(result.Error)
        };
    }
}

