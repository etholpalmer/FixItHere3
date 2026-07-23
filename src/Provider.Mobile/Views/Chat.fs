module FixItHere.Provider.Views.Chat

open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Graphics
open Fabulous
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared

/// A message bubble.
///
/// Alignment carries the speaker — the convention every messaging app the
/// audience has ever used — and the honey wash is the secondary cue rather than
/// the primary one, so the screen still reads correctly in greyscale.
///
/// `Border` with an explicit `strokeThickness` rather than a hairline: on a
/// phone at arm's length a 1px edge disappears, and an edgeless tinted rectangle
/// reads as a highlight, not as something someone said.
let inline private bubble (mine: bool) (content: WidgetBuilder<Msg, #IFabView>) =
    Border(content)
        .stroke(if mine then Theme.brandEdge else Theme.surfaceEdge)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusBubble))
        .background(if mine then Theme.brandWash else Theme.surface)
        .padding(Thickness(14., 10., 14., 10.))
        .horizontalOptions(if mine then LayoutOptions.End else LayoutOptions.Start)

let view (model: Model) (jobId: int) =
    let jobMessages = model.Messages |> List.filter (fun m -> m.JobId = jobId)
    let job = model.Jobs |> List.tryFind (fun j -> j.Id = jobId)
    let title = job |> Option.map (fun j -> j.CustomerName) |> Option.defaultValue "Chat"
    let subtitle =
        if model.CustomerTyping then "typing…"
        else job |> Option.map (fun j -> j.ServiceName) |> Option.defaultValue ""

    // Id of the most recent message I sent — the one the "Seen" marker attaches to.
    let lastMineId =
        jobMessages
        |> List.filter (fun m -> model.Session |> Option.exists (fun s -> isSelf s m.SenderId m.SenderRole))
        |> List.tryLast
        |> Option.map (fun m -> m.Id)

    let draft = draftFor model.ChatDrafts jobId

    (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {

        // ---- header: who you are talking to, and whether they are there ----
        (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Auto ]) {
            Button("‹", GoBack)
                .font(size = Theme.Font.title1)
                .textColor(Theme.brand)
                .width(Theme.touchTarget).height(Theme.touchTarget)
                .gridColumn(0).gridRowSpan(2)
            Label(title)
                .font(size = 17., attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(1).gridRow(0)
            Label(subtitle)
                .font(size = 13.)
                .textColor(if model.CustomerTyping then Theme.brand else Theme.inkMuted)
                .gridColumn(1).gridRow(1)
        })
            .padding(Thickness(8., 6., Theme.gutter, 10.))
            .gridRow(0)

        // ---- transcript ----------------------------------------------------
        (ScrollView(
            (VStack(spacing = Theme.gapTight) {
                if List.isEmpty jobMessages then
                    // Teaches the interface rather than announcing emptiness.
                    Label("No messages yet")
                        .font(size = 17., attributes = FontAttributes.Bold)
                        .textColor(Theme.ink)
                        .centerTextHorizontal()
                    Label("Send a note about access, parking, or anything you need before you set off.")
                        .font(size = 15.)
                        .textColor(Theme.inkMuted)
                        .centerTextHorizontal()
                        .padding(Thickness(24., 4., 24., 0.))

                for m in jobMessages do
                    let mine = model.Session |> Option.exists (fun s -> isSelf s m.SenderId m.SenderRole)
                    let seen =
                        mine && Some m.Id = lastMineId
                        && (match model.SeenUpToMessageId with Some w -> m.Id <= w | None -> false)

                    // Flat, two widgets per message. Fabulous CE rejects a
                    // nested VStack inside a `for`, so the bubble and its meta
                    // line are siblings that share an alignment.
                    if System.String.IsNullOrEmpty m.PhotoBase64 then
                        bubble mine (
                            Label(m.Text)
                                .font(size = 17.)
                                .textColor(if mine then Theme.brandInk else Theme.ink))
                    else
                        bubble mine (
                            Label("📷 Photo")
                                .font(size = 17.)
                                .textColor(if mine then Theme.brandInk else Theme.ink))

                    Label(
                        if seen then sprintf "%s · Seen" (Format.clockTime m.SentAt)
                        elif mine then Format.clockTime m.SentAt
                        else sprintf "%s · %s" m.SenderName (Format.clockTime m.SentAt))
                        .font(size = 11.)
                        .textColor(Theme.inkMuted)
                        .horizontalOptions(if mine then LayoutOptions.End else LayoutOptions.Start)
                        .padding(Thickness(6., 0., 6., 6.))
            })
                .padding(Thickness(Theme.gutter, 4., Theme.gutter, 8.))))
            .gridRow(1)

        // ---- composer ------------------------------------------------------
        (Grid(coldefs = [ Auto; Star; Auto ], rowdefs = [ Auto ]) {
            Button("📷", PickAndSendPhoto jobId)
                .font(size = 20.)
                .width(Theme.touchTarget).height(Theme.touchTarget)
                .gridColumn(0)

            // The field is the reason this row exists, so it takes the Star
            // column. As an HStack child it collapsed to the width of its own
            // text and there was physically nowhere to type.
            (Border(
                Entry(draft, fun t -> ChatDraftChanged (jobId, t))
                    .font(size = 17.)
                    .textColor(Theme.ink)
                    .placeholder("Message")
                    .placeholderColor(Theme.inkMuted)
                    )
                .stroke(Theme.surfaceEdge)
                .strokeThickness(Theme.strokeThick)
                .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
                .background(Theme.page)
                .padding(Thickness(12., 2., 12., 2.)))
                .gridColumn(1)

            // Primary action: filled, so it reads as the one thing to press.
            // Disabled until there is something to send — a Send that does
            // nothing is worse than a Send that is visibly not ready.
            (Border(
                Button("Send", SendChatMessage (jobId, draft, null))
                    .font(size = 17., attributes = FontAttributes.Bold)
                    .textColor(if System.String.IsNullOrWhiteSpace draft then Theme.inkMuted else Theme.onBrand)
                    )
                .stroke(if System.String.IsNullOrWhiteSpace draft then Theme.surfaceEdge else Theme.brand)
                .strokeThickness(Theme.strokeThick)
                .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
                .background(if System.String.IsNullOrWhiteSpace draft then Theme.surface else Theme.brand)
                .padding(Thickness(4., 0., 4., 0.)))
                .gridColumn(2)
        })
            .columnSpacing(8.)
            .padding(Thickness(Theme.gutter, 8., Theme.gutter, 8.))
            .background(Theme.surface)
            .gridRow(2)
    })
        .background(Theme.page)
