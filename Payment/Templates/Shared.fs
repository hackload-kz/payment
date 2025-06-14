// SPDX-FileCopyrightText: 2025 - HackLoad 2025 team
//
// SPDX-License-Identifier: MIT
module Payment.Templates.Shared

open System
open Microsoft.AspNetCore.Http
open Payment.Tools

module layout =
    open Oxpecker.ViewEngine
    open Oxpecker.Htmx

    let html (content: HtmlElement) (ctx: HttpContext)  =
        let flashMessage = getFlashedMessage ctx

        html(lang="") {
            head() {
                title() { "HackLoad 2025 - Payment system" }
                script(src="https://unpkg.com/htmx.org@1.9.10",
                    crossorigin="anonymous")
                link(rel="stylesheet", href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.6/dist/css/bootstrap.min.css", integrity="sha384-4Q6Gf2aSP4eDXB8Miphtr37CMZZQ5oXLH2yaXMJ2w8e2ZtHTl7GptT4jmndRuHDT", crossorigin="anonymous")
                link(rel="stylesheet", href="/site.css")
            }
            body(class' = "container",
                 hxBoost=true) {
                main() {
                    header(class'="text-center") {
                        h1() {
                            span(class'="text-uppercase") { "HackLoad 2025" }
                        }
                        h2() { "Payment system" }
                        if String.IsNullOrEmpty flashMessage |> not then
                            div(class'="alert alert-success fadeOut") { flashMessage }
                    }
                    hr()
                    content
                }
            }
        }