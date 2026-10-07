using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto.Waitlist;
using BookingApi.Service;
using BookingApi.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class WaitlistControllerTests
{
    private const int CallerId = 1;

    private readonly Mock<IWaitlistService> _mockWaitlistService = new();
    private readonly WaitlistController _controller;

    public WaitlistControllerTests()
    {
        _controller = new WaitlistController(_mockWaitlistService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser("User", CallerId) }
            }
        };
    }

    private void WithdrawReturns(WithdrawWaitlistResult result) =>
        _mockWaitlistService
            .Setup(s => s.WithdrawAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void GetMineReturns(IList<WaitlistEntryResponseDto> entries) =>
        _mockWaitlistService
            .Setup(s => s.GetMineAsync(It.IsAny<int>(), It.IsAny<WaitlistStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

    // ---------- GET me ----------

    [Fact]
    public async Task GetMine_HasEntries_ReturnsOkWithThem()
    {
        IList<WaitlistEntryResponseDto> entries = new List<WaitlistEntryResponseDto>
        {
            new(1, 5, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1),
                WaitlistStatus.Waiting, DateTime.UtcNow)
        };
        GetMineReturns(entries);

        var result = await _controller.GetMine(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(entries, ok.Value);
    }

    [Fact]
    public async Task GetMine_NoEntries_ReturnsOkWithEmptyList_NotNotFound()
    {
        GetMineReturns(new List<WaitlistEntryResponseDto>());

        var result = await _controller.GetMine(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsAssignableFrom<IEnumerable<WaitlistEntryResponseDto>>(ok.Value);
        Assert.Empty(body);
    }

    [Fact]
    public async Task GetMine_PassesTheCallersIdAndTheStatusFilterToTheService()
    {
        GetMineReturns(new List<WaitlistEntryResponseDto>());

        await _controller.GetMine(WaitlistStatus.Waiting, CancellationToken.None);

        _mockWaitlistService.Verify(
            s => s.GetMineAsync(CallerId, WaitlistStatus.Waiting, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------- POST {id}/withdraw ----------

    [Fact]
    public async Task Withdraw_Success_ReturnsNoContent()
    {
        WithdrawReturns(WithdrawWaitlistResult.Success);

        var result = await _controller.Withdraw(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Withdraw_NotFound_ReturnsNotFound()
    {
        WithdrawReturns(WithdrawWaitlistResult.NotFound);

        var result = await _controller.Withdraw(1, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Withdraw_NotWaiting_ReturnsConflict()
    {
        WithdrawReturns(WithdrawWaitlistResult.NotWaiting);

        var result = await _controller.Withdraw(1, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Withdraw_PassesTheCallersIdToTheService()
    {
        // The id must come from the token's claims, never from anything the client sends.
        WithdrawReturns(WithdrawWaitlistResult.Success);

        await _controller.Withdraw(42, CancellationToken.None);

        _mockWaitlistService.Verify(
            s => s.WithdrawAsync(42, CallerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}