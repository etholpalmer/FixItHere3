module FixItHere.Provider.Views.Login

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

/// See the customer app's Login for why this is a credential form and not a
/// list of businesses to tap.
let view (model: Model) =
    (VStack(spacing = 14.) {
        Label("FixItHere").font(size = 34., attributes = Microsoft.Maui.Controls.FontAttributes.Bold)
        Label("Provider sign in").font(size = 16.)

        Label("Email").font(size = 13.)
        Entry(model.LoginEmail, LoginEmailChanged).keyboard(Microsoft.Maui.Keyboard.Email)

        Label("Password").font(size = 13.)
        Entry(model.LoginPassword, LoginPasswordChanged).isPassword(true)

        Button((if model.SigningIn then "Signing in…" else "Sign in"), SignIn)
            .isEnabled(not model.SigningIn)
    }).centerVertical().padding(28.)
