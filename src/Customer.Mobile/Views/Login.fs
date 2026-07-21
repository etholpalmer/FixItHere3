module FixItHere.Customer.Views.Login

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

/// Credential sign-in rather than a name picker. The fields are prefilled with
/// the primary demo account so an operator is one tap from signing in, and the
/// email field is how you switch accounts — there is no list to pick from.
/// Nested VStacks are ambiguous inside a Fabulous computation expression
/// (FS0792), so the form is flat.
let view (model: Model) =
    (VStack(spacing = 14.) {
        Label("FixItHere").font(size = 34., attributes = Microsoft.Maui.Controls.FontAttributes.Bold)
        Label("Sign in to book a service").font(size = 16.)

        Label("Email").font(size = 13.)
        Entry(model.LoginEmail, LoginEmailChanged).keyboard(Microsoft.Maui.Keyboard.Email)

        Label("Password").font(size = 13.)
        Entry(model.LoginPassword, LoginPasswordChanged).isPassword(true)

        Button((if model.SigningIn then "Signing in…" else "Sign in"), SignIn)
            .isEnabled(not model.SigningIn)
    }).centerVertical().padding(28.)
