using BookingApi.Data;
using BookingApi.Dto.Waitlist;
using BookingApi.Helpers;
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
        [ProducesResponseType<List<WaitlistEntryResponseDto>>(StatusCodes.Status200OK, "application/json")]
        public async Task<IActionResult>  GetMine([FromQuery] WaitlistStatus? status, CancellationToken token)
        {
            var userId = User.GetUserId();
            var result = await waitlistService.GetMineAsync(userId, status, token);

            return Ok(result);
        }

        [HttpPost("{id:int}/withdraw")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
        public async Task<IActionResult> Withdraw(int id, CancellationToken token)
        {
            var userId = User.GetUserId();
            var result = await waitlistService.WithdrawAsync(id, userId, token);

            return result switch
            {
                WithdrawWaitlistResult.NotFound => NotFound("Waitlist Entry not found."),
                WithdrawWaitlistResult.NotWaiting => Conflict("This waitlist entry is no longer waiting: it has already been promoted or has expired."),
                WithdrawWaitlistResult.Success => NoContent(),
                _ => Problem()
            };
        }
    }
}
