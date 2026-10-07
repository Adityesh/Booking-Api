using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Service;
using BookingApi.Tests.Helpers;
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
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser("Admin") }
            }
        };
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
            .ReturnsAsync(false);

        var result = await _controller.Update(1, new UpdateResourceDto("Name", 5, ResourceType.Equipment));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Update_ResourceExists_ReturnsNoContent()
    {
        _mockResourceService
            .Setup(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<UpdateResourceDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.Update(1, new UpdateResourceDto("Name", 5, ResourceType.Equipment));

        Assert.IsType<NoContentResult>(result);
    }
}