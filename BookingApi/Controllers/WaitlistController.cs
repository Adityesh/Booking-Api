using BookingApi.Data;
using BookingApi.Dto.Waitlist;
using BookingApi.Extensions;
using BookingApi.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class WaitlistController(IWaitlistService waitlistService) : ControllerBase
    {
        [HttpGet("me")]
        public async Task<IActionResult>  GetMine([FromQuery] WaitlistStatus? status, CancellationToken token)
        {
            var userId = User.GetUserId();
            var result = await waitlistService.GetMineAsync(userId, status, token);

            return Ok(result);
        }

        [HttpPost("{id:int}/withdraw")]
        public async Task<IActionResult> Withdraw(int id, CancellationToken token)
        {
            var userId = User.GetUserId();
            var result = await waitlistService.WithdrawAsync(id, userId, token);

            return result switch
            {
                WithdrawWaitlistResult.NotFound => NotFound("Waitlist Entry not found."),
                WithdrawWaitlistResult.NotWaiting => Conflict("Waitlist not valid"),
                WithdrawWaitlistResult.Success => NoContent(),
                _ => Problem()
            };
        }
    }
}
