module FixItHere.Backend.Auth

/// DEMO CREDENTIALS — deliberately not a security mechanism.
///
/// This prototype issues tokens of the form "fake-customer-1"; there is no
/// session, no expiry and no authorization anywhere. The only reason a password
/// exists at all is believability: a sign-in that accepts *any* password is a
/// tell the moment someone tests it, and a name-picker login is worse.
///
/// So: one shared password per role, compared in plain text, stated in the
/// README. It is deliberately NOT hashed and NOT stored per-user — either would
/// imply a real credential store this prototype does not have and must not
/// pretend to have.
let passwordFor (role: string) =
    if role = "Provider" then "Provider1!" else "Customer1!"

/// Emails are derived from seed data rather than stored per-account, keeping the
/// seed deterministic (two boots must produce identical rows).
let private slug (s: string) =
    s.ToLowerInvariant()
     |> Seq.filter System.Char.IsLetterOrDigit
     |> Seq.toArray
     |> System.String

/// Consumer domains, varied by index so the list does not read as generated.
let private domains = [| "gmail.com"; "outlook.com"; "icloud.com"; "yahoo.ca" |]

let customerEmail (index: int) (name: string) =
    sprintf "%s@%s" (slug name) domains.[index % domains.Length]

let providerEmail (businessName: string) =
    sprintf "contact@%s.ca" (slug businessName)
