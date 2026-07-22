module FixItHere.Provider.Views.Chat

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider
open FixItHere.Shared

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
                        if mine && Some m.Id = lastMineId
                           && (match model.SeenUpToMessageId with Some w -> m.Id <= w | None -> false)
                        then " ✓✓ seen" else ""
                    // A conversation with no clock reads as a transcript, not a chat.
                    let stamp = Format.clockTime m.SentAt
                    let body = if System.String.IsNullOrEmpty m.PhotoBase64 then m.Text else "[photo]"
                    VStack(spacing = 0.) {
                        Label(sprintf "%s: %s" prefix body)
                        Label(sprintf "%s%s" stamp seenSuffix).font(size = 11.)
                    }
                if model.CustomerTyping then
                    Label("customer is typing…")
            })
        )).gridRow(1)
        (HStack(spacing = 8.) {
            Entry(draftFor model.ChatDrafts jobId, fun t -> ChatDraftChanged (jobId, t))
            Button("Send", SendChatMessage (jobId, draftFor model.ChatDrafts jobId, null))
            Button("📷", PickAndSendPhoto jobId)
        }).gridRow(2)
    }).padding(12.)
