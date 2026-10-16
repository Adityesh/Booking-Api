using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.Resource;
using BookingApi.Service;
using BookingApi.Tests.Helpers;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class ResourceControllerTests
{
    private readonly Mock<IResourceService> _mockResourceService;
    private readonly ResourceController _controller;

    public ResourceControllerTests()
    {
        _mockResourceService = new Mock<IResourceService>();
        _controller = new ResourceController(_mockResourceService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser("Admin", 1) }
            }
        };
    }

    // These tests are about the controller's branching after validation, so the validator always passes.
    // Mocks the real interface member, IValidator<T>.ValidateAsync(T, CancellationToken).
    private static IValidator<T> PassingValidator<T>()
    {
        var mock = new Mock<IValidator<T>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return mock.Object;
    }

    [Fact]
    public async Task GetById_ResourceDoesNotExist_ReturnsNotFound()
    {
        _mockResourceService
            .Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceResponseDto?)null);

        var result = await _controller.GetById(1);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetById_ResourceExists_ReturnsOk()
    {
        _mockResourceService
            .Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceResponseDto(1, "Test Name", ResourceType.Equipment, 5, true));

        var result = await _controller.GetById(1);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Update_ResourceDoesNotExist_ReturnsNotFound()
    {
        _mockResourceService
            .Setup(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<UpdateResourceDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResourceUpdateResult.NotFound);

        var result = await _controller.Update(1, new UpdateResourceDto("Name", 5, ResourceType.Equipment),
            PassingValidator<UpdateResourceDto>());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Update_ResourceExists_ReturnsNoContent()
    {
        _mockResourceService
            .Setup(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<UpdateResourceDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResourceUpdateResult.Updated);

        var result = await _controller.Update(1, new UpdateResourceDto("Name", 5, ResourceType.Equipment),
            PassingValidator<UpdateResourceDto>());

        Assert.IsType<NoContentResult>(result);
    }
}