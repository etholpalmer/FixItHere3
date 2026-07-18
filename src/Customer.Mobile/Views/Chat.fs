module FixItHere.Customer.Views.Chat

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (jobId: int) =
    (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
        (VStack(spacing = 4.) {
            Button("← Back", GoBack)
            Label("Chat").font(size = 22.)
        }).gridRow(0)
        (ScrollView(
            (VStack(spacing = 6.) {
                for m in model.Messages |> List.filter (fun m -> m.JobId = jobId) do
                    let mine = model.Session |> Option.exists (fun s -> s.UserId = m.SenderId)
                    let prefix = if mine then "You" else m.SenderName
                    if System.String.IsNullOrEmpty m.PhotoBase64 then
                        Label(sprintf "%s: %s" prefix m.Text)
                    else
                        Label(sprintf "%s: [photo]" prefix)
            })
        )).gridRow(1)
        (HStack(spacing = 8.) {
            Entry(model.ChatDraft, ChatDraftChanged)
            Button("Send", SendChatMessage (jobId, model.ChatDraft, null))
            Button("📷", PickAndSendPhoto jobId)
        }).gridRow(2)
    }).padding(12.)
