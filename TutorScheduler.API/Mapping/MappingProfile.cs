using AutoMapper;
using TutorScheduler.Core.DTOs.Bookings;
using TutorScheduler.Core.DTOs.LessonSlots;
using TutorScheduler.Core.DTOs.Notifications;
using TutorScheduler.Core.DTOs.Subjects;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.API.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Subject, SubjectResponseDto>();

        CreateMap<LessonSlot, LessonSlotResponseDto>()
            .ForMember(dest => dest.TutorName, opt => opt.MapFrom(src => src.Tutor.FullName))
            .ForMember(dest => dest.SubjectName, opt => opt.MapFrom(src => src.Subject.Name))
            .ForMember(dest => dest.StudentName, opt => opt.MapFrom(src =>
                src.Bookings
                    .Where(b => b.Status == BookingStatus.Active)
                    .Select(b => b.Student.FullName)
                    .FirstOrDefault()));

        CreateMap<Booking, BookingResponseDto>()
            .ForMember(dest => dest.SubjectName, opt => opt.MapFrom(src => src.LessonSlot.Subject.Name))
            .ForMember(dest => dest.TutorName, opt => opt.MapFrom(src => src.LessonSlot.Tutor.FullName))
            .ForMember(dest => dest.StartTimeUtc, opt => opt.MapFrom(src => src.LessonSlot.StartTimeUtc))
            .ForMember(dest => dest.EndTimeUtc, opt => opt.MapFrom(src => src.LessonSlot.EndTimeUtc))
            .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.LessonSlot.Price));

        CreateMap<Notification, NotificationResponseDto>()
            .ForMember(dest => dest.SubjectName, opt => opt.MapFrom(src => src.LessonSlot.Subject.Name))
            .ForMember(dest => dest.TutorName, opt => opt.MapFrom(src => src.LessonSlot.Tutor.FullName))
            .ForMember(dest => dest.StartTimeUtc, opt => opt.MapFrom(src => src.LessonSlot.StartTimeUtc))
            .ForMember(dest => dest.EndTimeUtc, opt => opt.MapFrom(src => src.LessonSlot.EndTimeUtc))
            .ForMember(dest => dest.IsRead, opt => opt.MapFrom(src => src.ReadAtUtc != null));
    }
}
