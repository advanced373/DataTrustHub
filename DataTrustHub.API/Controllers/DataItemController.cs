using DataTrustHub.API.Data.DTOs;
using DataTrustHub.Application.Data.Create;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DataItemController : ControllerBase
    {
        private readonly ILogger<DataItemController> _logger;
        private readonly IMediator _mediator;

        public DataItemController(ILogger<DataItemController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        [HttpPost(Name = "CreateDataItem")]
        public async Task<IActionResult> CreateDataItem([FromBody] DataItemDto dataItemDto)
        {
            var result = await _mediator.Send(new CreateDataItemCommand(
                dataItemDto.Name,
                dataItemDto.Size,
                dataItemDto.Content,
                dataItemDto.OwnerUserId,
                dataItemDto.SecurityMarking));
            
            if (result.IsFailure)
            {
                return BadRequest(result.Error);
            }

            return Ok(new { Id = result.Value });
        }
    }
}

