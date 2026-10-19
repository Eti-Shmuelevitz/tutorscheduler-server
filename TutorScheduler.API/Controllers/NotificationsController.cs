using System.ComponentModel.DataAnnotations;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorScheduler.API.Auth;
using TutorScheduler.Core.DTOs.Common;
using TutorScheduler.Core.DTOs.Notifications;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API.Controllers;

// Any signed-in user's own notifications (today only students receive them).
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ApiControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IMapper _mapper;

    public NotificationsController(INotificationService notificationService, ICurrentUserService currentUser, IMapper mapper)
    {
        _notificationService = notificationService;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<PagedResponseDto<NotificationResponseDto>>> GetMine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await _notificationService.GetMinePagedAsync(_currentUser.UserId, page, pageSize, ct);

        return Ok(new PagedResponseDto<NotificationResponseDto>
        {
            Items = _mapper.Map<IReadOnlyList<NotificationResponseDto>>(items),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount(CancellationToken ct) =>
        Ok(new { count = await _notificationService.GetUnreadCountAsync(_currentUser.UserId, ct) });

    [HttpPost("{id:int}/read")]
    public async Task<ActionResult> MarkRead([FromRoute] int id, CancellationToken ct)
    {
        var result = await _notificationService.MarkReadAsync(_currentUser.UserId, id, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notificationService.MarkAllReadAsync(_currentUser.UserId, ct);
        return NoContent();
    }
}
