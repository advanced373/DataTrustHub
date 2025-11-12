using DataTrustHub.API.Clearance.DTOs;
using DataTrustHub.Application.Clearance.Create;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ClearanceController : ControllerBase
    {
        private readonly ILogger<ClearanceController> _logger;
        private readonly IMediator _mediator;

        public ClearanceController(ILogger<ClearanceController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        [HttpPost(Name = "CreateClearance")]
        public async Task<IActionResult> CreateClearance([FromBody] ClearanceDto clearanceDto)
        {
            var result = await _mediator.Send(new CreateClearanceForUserCommand(
                clearanceDto.UserId,
                clearanceDto.PolicyId,
                clearanceDto.Name,
                clearanceDto.ClassificationLevelName,
                clearanceDto.ClassificationLevelPriority));
            
            if (result.IsFailure)
            {
                return BadRequest(result.Error);
            }

            return Ok(new { Id = result.Value });
        }
    }
}

