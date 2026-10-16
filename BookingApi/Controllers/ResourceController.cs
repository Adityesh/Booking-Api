using BookingApi.Dto;
using BookingApi.Dto.Resource;
using BookingApi.Extensions;
using BookingApi.Service;
using FluentValidation;
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
        [ProducesResponseType<List<ResourceResponseDto>>(StatusCodes.Status200OK, "application/json")]
        public async Task<IActionResult> GetActive(CancellationToken token = default)
        {
            var result = await resourceService.GetAllAsync(false, token);
            return Ok(result);
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType<List<ResourceResponseDto>>(StatusCodes.Status200OK, "application/json")]
        public async Task<IActionResult> GetAll(CancellationToken token = default)
        {
            var result = await resourceService.GetAllAsync(true, token);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        [Authorize]
        [ProducesResponseType<ResourceResponseDto>(StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        public async Task<IActionResult> GetById(int id, CancellationToken token = default)
        {
            var isAdmin = User.IsInRole("Admin");
            var result = await resourceService.GetByIdAsync(id, isAdmin, token);
            return result == null ? NotFound("Resource not found") : Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType<ResourceResponseDto>(StatusCodes.Status201Created, "application/json")]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        public async Task<IActionResult> Create(CreateResourceDto dto, IValidator<CreateResourceDto> validator,
            CancellationToken token = default)
        {
            var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

            var userId = User.GetUserId();
            var result = await resourceService.CreateAsync(dto, userId, token);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        public async Task<IActionResult> Update(int id, UpdateResourceDto dto, IValidator<UpdateResourceDto> validator,
            CancellationToken token = default)
        {
            var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

            var userId = User.GetUserId();
            var result = await resourceService.UpdateAsync(id, dto, userId, token);
            return result switch
            {
                // Unchanged is a success too: the resource is in exactly the state the client asked for.
                ResourceUpdateResult.Updated or ResourceUpdateResult.Unchanged => NoContent(),
                ResourceUpdateResult.NotFound => NotFound("Resource not found"),
                _ => Problem()
            };
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        public async Task<IActionResult> Delete(int id, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var result = await resourceService.DeleteAsync(id, userId, token);
            return result ? NoContent() : NotFound("Resource not found");
        }
    }
}
