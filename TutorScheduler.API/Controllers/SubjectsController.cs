using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorScheduler.Core.DTOs.Subjects;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API.Controllers;

[Route("api/subjects")]
public class SubjectsController : ApiControllerBase
{
    private readonly ISubjectService _subjectService;
    private readonly IMapper _mapper;

    public SubjectsController(ISubjectService subjectService, IMapper mapper)
    {
        _subjectService = subjectService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SubjectResponseDto>>> GetAll(CancellationToken ct)
    {
        var subjects = await _subjectService.GetAllAsync(ct);
        return Ok(_mapper.Map<IReadOnlyList<SubjectResponseDto>>(subjects));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SubjectResponseDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var subject = await _subjectService.GetByIdAsync(id, ct);
        if (subject is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<SubjectResponseDto>(subject));
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult<SubjectResponseDto>> Create([FromBody] SubjectRequestDto request, CancellationToken ct)
    {
        var result = await _subjectService.CreateAsync(request.Name, ct);
        if (!result.IsSuccess)
        {
            return HandleFailure(result.Status, result.Error);
        }

        var response = _mapper.Map<SubjectResponseDto>(result.Value);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult> Update([FromRoute] int id, [FromBody] SubjectRequestDto request, CancellationToken ct)
    {
        var result = await _subjectService.UpdateAsync(id, request.Name, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Tutor))]
    public async Task<ActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        var result = await _subjectService.DeleteAsync(id, ct);
        return result.IsSuccess ? NoContent() : HandleFailure(result.Status, result.Error);
    }
}
