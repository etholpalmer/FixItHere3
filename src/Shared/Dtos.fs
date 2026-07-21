namespace FixItHere.Shared.Dtos

[<CLIMutable>]
type Envelope<'t> = { Success: bool; Data: 't; Error: string }

module Envelope =
    let ok data = { Success = true; Data = data; Error = null }
    let fail (msg: string) : Envelope<obj> = { Success = false; Data = null; Error = msg }

[<CLIMutable>]
type LoginRequest = { Role: string; Name: string }

[<CLIMutable>]
type LoginResponse = { Token: string; UserId: int; Role: string; DisplayName: string }

[<CLIMutable>]
type ServiceDto = { Id: int; Name: string }

[<CLIMutable>]
type ProviderDto =
    { Id: int; BusinessName: string; ServiceId: int; ServiceName: string
      Rating: float; RatingCount: int; Lat: float; Lng: float
      Online: bool; Vehicle: string; PhotoUrl: string }

[<CLIMutable>]
type JobDto =
    { Id: int; CustomerId: int; CustomerName: string
      ProviderId: int; ProviderName: string
      ServiceId: int; ServiceName: string
      State: string; Price: decimal; ScheduledFor: string
      Lat: float; Lng: float; Address: string }

[<CLIMutable>]
type CreateJobRequest =
    { CustomerId: int; ProviderId: int; ServiceId: int
      ScheduleChoice: string; Lat: float; Lng: float; Address: string }

/// Customer and Provider ids are independent sequences that both start at 1,
/// so a bare SenderId is ambiguous — customer 1 and provider 1 are different
/// actors. Every identity that crosses an app boundary carries its role.
[<CLIMutable>]
type MessageDto =
    { Id: int; JobId: int; SenderId: int; SenderRole: string; SenderName: string
      Text: string; PhotoBase64: string; SentAt: string; Seen: bool }

[<CLIMutable>]
type SendMessageRequest =
    { JobId: int; SenderId: int; SenderRole: string; Text: string; PhotoBase64: string }

/// Rater/Ratee carry a role for the same reason MessageDto.SenderRole does:
/// customer and provider ids are independent sequences that both start at 1,
/// so an id alone cannot say who was rated. Without the roles, a provider
/// rating a customer moved that customer's id-twin *provider*'s public average.
[<CLIMutable>]
type RatingDto =
    { Id: int; JobId: int
      RaterId: int; RaterRole: string
      RateeId: int; RateeRole: string
      Stars: int; Comment: string }

[<CLIMutable>]
type CreateRatingRequest =
    { JobId: int
      RaterId: int; RaterRole: string
      RateeId: int; RateeRole: string
      Stars: int; Comment: string }

[<CLIMutable>]
type LocationDto = { ProviderId: int; Lat: float; Lng: float; UpdatedAt: string }

[<CLIMutable>]
type UpdateLocationRequest = { ProviderId: int; Lat: float; Lng: float }

[<CLIMutable>]
type PaymentRequest = { JobId: int }

[<CLIMutable>]
type PaymentResult = { JobId: int; Amount: decimal; Status: string }
