using DataTrustHub.API.User.DTOs;
using DataTrustHub.Application.User.Register;
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
        
    }
}
