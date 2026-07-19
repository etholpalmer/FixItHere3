module FixItHere.Customer.Views.Chat

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (jobId: int) =
    let jobMessages = model.Messages |> List.filter (fun m -> m.JobId = jobId)
    // Id of the most recent message I sent — the one the "✓✓ seen" marker attaches to.
    let lastMineId =
        jobMessages
        |> List.filter (fun m -> model.Session |> Option.exists (fun s -> isSelf s m.SenderId m.SenderRole))
        |> List.tryLast
        |> Option.map (fun m -> m.Id)
    (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
        (VStack(spacing = 4.) {
            Button("← Back", GoBack)
            Label("Chat").font(size = 22.)
        }).gridRow(0)
        (ScrollView(
            (VStack(spacing = 6.) {
                for m in jobMessages do
                    let mine = model.Session |> Option.exists (fun s -> isSelf s m.SenderId m.SenderRole)
                    let prefix = if mine then "You" else m.SenderName
                    let seenSuffix =
                        if mine && Some m.Id = lastMineId && model.MessagesSeen then " ✓✓ seen" else ""
                    if System.String.IsNullOrEmpty m.PhotoBase64 then
                        Label(sprintf "%s: %s%s" prefix m.Text seenSuffix)
                    else
                        Label(sprintf "%s: [photo]%s" prefix seenSuffix)
                if model.ProviderTyping then
                    Label("provider is typing…")
            })
        )).gridRow(1)
        (HStack(spacing = 8.) {
            Entry(model.ChatDraft, ChatDraftChanged)
            Button("Send", SendChatMessage (jobId, model.ChatDraft, null))
            Button("📷", PickAndSendPhoto jobId)
        }).gridRow(2)
    }).padding(12.)
