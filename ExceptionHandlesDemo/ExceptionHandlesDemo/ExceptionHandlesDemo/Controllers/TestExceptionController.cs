using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Authentication;

namespace ExceptionHandlesDemo.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestExceptionController: ControllerBase
    {
        /// <summary>
        /// 测试 UnauthorizedAccessException
        /// </summary>
        [HttpGet("unauthorized-exception")]
        [AllowAnonymous]
        public IActionResult TestUnauthorizedException()
        {
            throw new UnauthorizedAccessException("这是一个测试的 UnauthorizedAccessException");
        }

        /// <summary>
        /// 测试 AuthenticationException
        /// </summary>
        [HttpGet("authentication-exception")]
        [AllowAnonymous]
        public IActionResult TestAuthenticationException()
        {
            throw new AuthenticationException("这是一个测试的 AuthenticationException");
        }

        /// <summary>
        /// 测试 一般异常
        /// </summary>
        [HttpGet("general-exception")]
        [AllowAnonymous]
        public IActionResult TestGeneralException()
        {
            throw new InvalidOperationException("这是一个测试的一般异常");
        }
    }
}
