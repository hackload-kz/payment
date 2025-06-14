module Payment.Tools
open Microsoft.AspNetCore.Http
open Oxpecker
open FSharp.Data
open Core

type MerchantsConfiguration = CsvProvider<"merchants.csv", Schema="Id=string,Password=string">

/// Loads merchants data from CSV file
let getMerchantsData (fileName: string) =
    let configuration = MerchantsConfiguration.Load(fileName)
    configuration.Rows
        |> Seq.map (fun c -> { merchant_id = c.Id; merchant_key = c.Password })
        |> Seq.toArray

let getFlashedMessage (ctx: HttpContext) =
    match ctx.Items.TryGetValue("message") with
    | true, msg ->
        ctx.Items.Remove("message") |> ignore
        string msg
    | _ ->
        match ctx.Request.Cookies.TryGetValue("message") with
        | true, NonNull msg ->
            ctx.Response.Cookies.Delete("message")
            msg
        | _ ->
            ""

let flash (msg: string) (ctx: HttpContext) =
    ctx.Items.Add("message", msg)
    ctx.Response.Cookies.Append("message", msg)

let writeHtml view ctx =
    htmlView (view ctx) ctx
