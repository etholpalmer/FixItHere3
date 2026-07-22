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

/// firstname.lastname@domain — what a real consumer address looks like. A bare
/// first name ("john@") reads as seed data.
let customerEmail (index: int) (fullName: string) =
    let parts = fullName.Split(' ')
    let local =
        if parts.Length >= 2 then sprintf "%s.%s" (slug parts.[0]) (slug parts.[parts.Length - 1])
        else slug fullName
    sprintf "%s@%s" local domains.[index % domains.Length]

let providerEmail (businessName: string) =
    sprintf "contact@%s.ca" (slug businessName)

/// Initials for an avatar: "John Reyes" -> "JR", "Mike's Plumbing" -> "MP".
let initials (name: string) =
    name.Split([| ' '; '\'' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.filter (fun w -> System.Char.IsLetter w.[0])
    |> Array.truncate 2
    |> Array.map (fun w -> System.Char.ToUpperInvariant w.[0])
    |> System.String

/// A deterministic, readable background per account. Hue is derived from the
/// name so the same person always gets the same colour, and the palette is
/// restricted to mid-lightness so white text stays legible on all of it.
let avatarSvg (name: string) =
    let hue = (abs (name.GetHashCode()) % 360)
    sprintf """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" width="96" height="96" role="img" aria-label="%s">
  <rect width="96" height="96" rx="48" fill="oklch(0.62 0.13 %d)"/>
  <text x="48" y="49" text-anchor="middle" dominant-baseline="central"
        font-family="system-ui, -apple-system, sans-serif" font-size="38" font-weight="600"
        fill="oklch(0.99 0 0)">%s</text>
</svg>""" (System.Net.WebUtility.HtmlEncode name) hue (initials name)
