module FixItHere.Provider.Views.RateCustomer

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let view (model: Model) (jobId: int) =
    ScrollView(
     (VStack(spacing = 16.) {
        Label("Rate your customer").font(size = 28.).centerTextHorizontal()
        (HStack(spacing = 4.) {
            for i in 1 .. 5 do
                Button((if i <= model.RatingStars then "★" else "☆"), StarsChanged i)
        }).centerHorizontal()
        Entry(model.RatingComment, RatingCommentChanged)
        Button("Submit", SubmitRating (jobId, model.RatingStars, model.RatingComment))
     }).centerVertical().padding(24.))
