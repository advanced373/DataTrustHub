using DataTrustHub.API.Data.DTOs;
using DataTrustHub.Application.Data.Create;
using DataTrustHub.Application.Data.Get;
using DataTrustHub.SharedKernel;
using System.Linq;
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

        [HttpGet(Name = "GetDataItems")]
        public async Task<IActionResult> GetDataItems()
        {
            var result = await _mediator.Send(new GetDataItemsQuery());

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var response = result.Value.Select(dataItem => new DataItemResponseDto
            {
                Id = dataItem.Id,
                Name = dataItem.Name,
                Size = dataItem.Size,
                Content = dataItem.Content,
                OwnerUserId = dataItem.OwnerUserId,
                SecurityMarking = dataItem.SecurityMarking
            });

            return Ok(response);
        }

        [HttpGet("{id:guid}", Name = "GetDataItemById")]
        public async Task<IActionResult> GetDataItemById(Guid id)
        {
            var result = await _mediator.Send(new GetDataItemByIdQuery(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var dataItem = result.Value;

            return Ok(new DataItemResponseDto
            {
                Id = dataItem.Id,
                Name = dataItem.Name,
                Size = dataItem.Size,
                Content = dataItem.Content,
                OwnerUserId = dataItem.OwnerUserId,
                SecurityMarking = dataItem.SecurityMarking
            });
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

        // Delete is handled exclusively by the VSA slice at DELETE /data/{id:guid}
        // (DataTrustHub.Features.DataManagement.DeleteDataItem), which enforces ownership
        // and soft-deletes. This legacy action was removed because it bypassed both checks.

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

