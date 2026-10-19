using System.ComponentModel.DataAnnotations;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorScheduler.API.Auth;
using TutorScheduler.Core.DTOs.Bookings;
using TutorScheduler.Core.DTOs.Common;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API.Controllers;

[Route("api/bookings")]
[Authorize(Roles = nameof(UserRole.Student))]
public class BookingsController : ApiControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly ICurrentUserService _currentUser;
    private readonly IMapper _mapper;

    public BookingsController(IBookingService bookingService, ICurrentUserService currentUser, IMapper mapper)
    {
        _bookingService = bookingService;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<PagedResponseDto<BookingResponseDto>>> GetMine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var studentId = _currentUser.UserId;
        var (items, totalCount) = await _bookingService.GetByStudentPagedAsync(studentId, page, pageSize, ct);

        return Ok(new PagedResponseDto<BookingResponseDto>
        {
            Items = _mapper.Map<IReadOnlyList<BookingResponseDto>>(items),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponseDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var studentId = _currentUser.UserId;
        var booking = await _bookingService.GetByIdAsync(studentId, id, ct);
        if (booking is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<BookingResponseDto>(booking));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel([FromRoute] int id, CancellationToken ct)
    {
        var studentId = _currentUser.UserId;
        var result = await _bookingService.CancelBookingAsync(studentId, id, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }
}
