using DataTrustHub.API.User.DTOs;
using DataTrustHub.Application.User.Get;
using DataTrustHub.Application.User.Register;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {

        private readonly ILogger<UserController> _logger;
        private readonly IMediator _mediator;

        public UserController(ILogger<UserController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        [HttpGet(Name = "GetUsers")]
        public async Task<IActionResult> GetUsers()
        {
            var result = await _mediator.Send(new GetUsersQuery());

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var response = result.Value.Select(user => new UserResponseDto
            {
                Id = user.Id,
                Email = user.Email
            });

            return Ok(response);
        }

        [HttpGet("{id:guid}", Name = "GetUserById")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            var result = await _mediator.Send(new GetUserByIdQuery(id));

            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            var user = result.Value;

            return Ok(new UserResponseDto
            {
                Id = user.Id,
                Email = user.Email
            });
        }

        [HttpPost(Name = "RegisterUser")]
        public async Task<IActionResult> CreateUser([FromBody] UserDto userDto)
        {
            var result = await _mediator.Send(new RegisterUserCommand(userDto.Email, userDto.Password));
            
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
