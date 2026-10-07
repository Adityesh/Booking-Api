using BookingApi.Dto;
using BookingApi.Extensions;
using BookingApi.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResourceController(IResourceService resourceService) : ControllerBase
    {
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetActive(CancellationToken token = default)
        {
            var result = await resourceService.GetAllAsync(false, token);
            return Ok(result);
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll(CancellationToken token = default)
        {
            var result = await resourceService.GetAllAsync(true, token);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken token = default)
        {
            var isAdmin = User.IsInRole("Admin");
            var result = await resourceService.GetByIdAsync(id, isAdmin, token);
            return result == null ? NotFound("Resource not found") : Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(CreateResourceDto dto, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var result = await resourceService.CreateAsync(dto, userId, token);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, UpdateResourceDto dto, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var result = await resourceService.UpdateAsync(id, dto, userId, token);
            return result ? NoContent() : NotFound("Resource not found");
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var result = await resourceService.DeleteAsync(id, userId, token);
            return result ? NoContent() : NotFound("Resource not found");
        }
    }
}
