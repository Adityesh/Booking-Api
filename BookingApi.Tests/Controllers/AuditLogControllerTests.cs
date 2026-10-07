using System.Reflection;
using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.AuditLog;
using BookingApi.Service;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class AuditLogControllerTests
{
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly Mock<IValidator<GetAuditLogDto>> _validator = new();
    private readonly AuditLogController _controller;

    private static readonly GetAuditLogDto Query = new(null, null, null, null, false);

    public AuditLogControllerTests()
    {
        _controller = new AuditLogController(_auditLogService.Object);
    }

    private void ValidatorReturns(ValidationResult result) =>
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<GetAuditLogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public void GetAuditLogs_IsRestrictedToTheAdminRole()
    {
        // The service never checks roles, so this attribute is the only thing keeping the audit log private.
        // An integration test proves it end to end later; this one stops it being deleted by accident.
        var method = typeof(AuditLogController).GetMethod(nameof(AuditLogController.GetAuditLogs))!;

        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public async Task GetAuditLogs_InvalidQuery_Returns400_AndNeverCallsTheService()
    {
        ValidatorReturns(new ValidationResult([new ValidationFailure("PageSize", "Page size must be in between 1 and 100")]));

        var result = await _controller.GetAuditLogs(Query, _validator.Object, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("PageSize", problem.Errors.Keys);
        _auditLogService.Verify(
            s => s.GetAuditLogsAsync(It.IsAny<GetAuditLogDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAuditLogs_ValidQuery_Returns200WithThePagedResult()
    {
        var page = new PagedResult<AuditLogResponseDto>(1, 20, 1,
            [new AuditLogResponseDto(5, 9, null, DateTime.UtcNow, ActionType.WaitlistEntryExpired)]);
        ValidatorReturns(new ValidationResult());
        _auditLogService
            .Setup(s => s.GetAuditLogsAsync(It.IsAny<GetAuditLogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await _controller.GetAuditLogs(Query, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(page, ok.Value);
        _auditLogService.Verify(s => s.GetAuditLogsAsync(Query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_NothingMatches_StillReturns200_NotA404()
    {
        var empty = new PagedResult<AuditLogResponseDto>(1, 20, 0, new List<AuditLogResponseDto>());
        ValidatorReturns(new ValidationResult());
        _auditLogService
            .Setup(s => s.GetAuditLogsAsync(It.IsAny<GetAuditLogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        var result = await _controller.GetAuditLogs(Query, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<PagedResult<AuditLogResponseDto>>(ok.Value);
        Assert.Empty(body.Data);
    }
}