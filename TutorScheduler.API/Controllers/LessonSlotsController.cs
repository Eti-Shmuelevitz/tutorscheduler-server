using System.ComponentModel.DataAnnotations;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TutorScheduler.API.Auth;
using TutorScheduler.Core.DTOs.Common;
using TutorScheduler.Core.DTOs.LessonSlots;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API.Controllers;

[Route("api/lessonslots")]
public class LessonSlotsController : ApiControllerBase
{
    private readonly ILessonSlotService _lessonSlotService;
    private readonly IBookingService _bookingService;
    private readonly ICurrentUserService _currentUser;
    private readonly IMapper _mapper;

    public LessonSlotsController(
        ILessonSlotService lessonSlotService,
        IBookingService bookingService,
        ICurrentUserService currentUser,
        IMapper mapper)
    {
        _lessonSlotService = lessonSlotService;
        _bookingService = bookingService;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    // Public/student-facing browse: only ever shows Available slots.
    [HttpGet]
    public async Task<ActionResult<PagedResponseDto<LessonSlotResponseDto>>> GetAvailable(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 20,
        [FromQuery] int? subjectId = null,
        [FromQuery] int? tutorId = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await _lessonSlotService.GetAvailablePagedAsync(
            page, pageSize, subjectId, tutorId, sortDescending, ct);

        return Ok(new PagedResponseDto<LessonSlotResponseDto>
        {
            Items = _mapper.Map<IReadOnlyList<LessonSlotResponseDto>>(items),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // Tutor's own management view: all statuses, own slots only.
    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult<PagedResponseDto<LessonSlotResponseDto>>> GetMine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tutorId = _currentUser.UserId;
        var (items, totalCount) = await _lessonSlotService.GetByTutorPagedAsync(tutorId, page, pageSize, ct);

        return Ok(new PagedResponseDto<LessonSlotResponseDto>
        {
            Items = _mapper.Map<IReadOnlyList<LessonSlotResponseDto>>(items),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LessonSlotResponseDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var slot = await _lessonSlotService.GetByIdAsync(id, ct);
        if (slot is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<LessonSlotResponseDto>(slot));
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult<LessonSlotResponseDto>> Create(
        [FromBody] LessonSlotCreateRequestDto request, CancellationToken ct)
    {
        var tutorId = _currentUser.UserId;
        var result = await _lessonSlotService.CreateAsync(
            tutorId, request.SubjectId, request.StartTimeUtc, request.EndTimeUtc, ct);

        if (!result.IsSuccess)
        {
            return HandleFailure(result.Status, result.Error);
        }

        // Re-fetch with Tutor/Subject included: CreateAsync's Result carries
        // the bare entity, but the response DTO needs the navigation data.
        var created = await _lessonSlotService.GetByIdAsync(result.Value!.Id, ct);
        var response = _mapper.Map<LessonSlotResponseDto>(created);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult> Update(
        [FromRoute] int id, [FromBody] LessonSlotUpdateRequestDto request, CancellationToken ct)
    {
        var tutorId = _currentUser.UserId;
        var result = await _lessonSlotService.UpdateAsync(tutorId, id, request.StartTimeUtc, request.EndTimeUtc, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        var tutorId = _currentUser.UserId;
        var result = await _lessonSlotService.DeleteAsync(tutorId, id, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }

    // Unlike DELETE, works on a booked slot too: the booking is cancelled and
    // the student is notified. The body (an optional reason) may be omitted.
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult> CancelByTutor(
        [FromRoute] int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LessonSlotCancelRequestDto? request,
        CancellationToken ct)
    {
        var tutorId = _currentUser.UserId;
        var result = await _lessonSlotService.CancelByTutorAsync(tutorId, id, request?.Reason, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }

    // The contested operation. A 409 here means another student won the race.
    [HttpPost("{id:int}/bookings")]
    [Authorize(Roles = nameof(UserRole.Student))]
    public async Task<ActionResult> Book([FromRoute] int id, CancellationToken ct)
    {
        var studentId = _currentUser.UserId;
        var result = await _bookingService.BookSlotAsync(studentId, id, ct);

        if (!result.IsSuccess)
        {
            return HandleFailure(result.Status, result.Error);
        }

        return CreatedAtAction(
            nameof(BookingsController.GetById),
            "Bookings",
            new { id = result.Value!.Id },
            new { bookingId = result.Value!.Id });
    }
}
