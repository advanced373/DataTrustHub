using DataTrustHub.API.Clearance.DTOs;
using DataTrustHub.API.Shared.DTOs;
using DataTrustHub.Application.Clearance.Create;
using DataTrustHub.Application.Clearance.Get;
using DataTrustHub.SharedKernel;
using System.Linq;
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

        [HttpGet(Name = "GetClearances")]
        public async Task<IActionResult> GetClearances()
        {
            var result = await _mediator.Send(new GetClearancesQuery());

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var response = result.Value.Select(clearance => new ClearanceResponseDto
            {
                Id = clearance.Id,
                Name = clearance.Name,
                UserId = clearance.UserId,
                PolicyId = clearance.PolicyId,
                ClassificationLevel = new ClassificationLevelDto
                {
                    Id = clearance.ClassificationLevel.Id,
                    Name = clearance.ClassificationLevel.Name,
                    Priority = clearance.ClassificationLevel.Priority
                }
            });

            return Ok(response);
        }

        [HttpGet("{id:guid}", Name = "GetClearanceById")]
        public async Task<IActionResult> GetClearanceById(Guid id)
        {
            var result = await _mediator.Send(new GetClearanceByIdQuery(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var clearance = result.Value;

            return Ok(new ClearanceResponseDto
            {
                Id = clearance.Id,
                Name = clearance.Name,
                UserId = clearance.UserId,
                PolicyId = clearance.PolicyId,
                ClassificationLevel = new ClassificationLevelDto
                {
                    Id = clearance.ClassificationLevel.Id,
                    Name = clearance.ClassificationLevel.Name,
                    Priority = clearance.ClassificationLevel.Priority
                }
            });
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

        private IActionResult HandleFailure<T>(Result<T> result) => result.Error.Type switch
        {
            ErrorType.NotFound => NotFound(result.Error),
            _ => BadRequest(result.Error)
        };
    }
}

