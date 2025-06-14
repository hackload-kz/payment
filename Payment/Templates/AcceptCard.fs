module Payment.Templates.AcceptCard

open Oxpecker.ViewEngine
open Oxpecker.Htmx
open Oxpecker.ViewEngine.Aria
open Shared

let invalid_transaction (transaction_id: string) =
    Fragment() {
        p() {
            $"Transaction #{transaction_id} was not found"
        }
    }
    |> layout.html

let html q  =
    Fragment() {
        form(class'="needs-validation col col-sm-5 mx-auto") {            
            div(class'= "row gy-3"){
                div(class'= "col-md-6"){
                    label(for'="cc-name", class'="form-label") { "Name on card" }
                    input(type'="text", class'="form-control", id="cc-name", placeholder="", required=true)
                    small(class'="text-body-secondary") { "Full name as displayed on card" }
                    div(class'="invalid-feedback") { "Name on card is required" }
                }
                div(class'= "col-md-6"){
                    label(for'="cc-number", class'="form-label") { "Credit card number" }
                    input(type'="text", class'="form-control", id="cc-number", placeholder="", required=true)
                    div(class'="invalid-feedback") { "Credit card number is required" }
                }
                div(class'= "col-md-3"){
                    label(for'="cc-expiration", class'="form-label") { "Expiration" }
                    input(type'="text", class'="form-control", id="cc-expiration", placeholder="", required=true)
                    div(class'="invalid-feedback") { "Expiration date required" }
                }
                div(class'= "col-md-3"){
                    label(for'="cc-ccv", class'="form-label") { "CVV" }
                    input(type'="text", class'="form-control", id="cc-ccv", placeholder="", required=true)
                    div(class'="invalid-feedback") { "Security code required" }
                }

            }
            hr (class'="my-4")
            button(type'="submit", class'="w-100 btn btn-primary btn-lg") { "Continue to checkout" }
        }
    }
    |> layout.html