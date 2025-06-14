module Generators

open System
open Payment.Core

open FsCheck.FSharp

(*    
    This module contains generators for various domain models used in the payment system.
    It includes valid merchants, valid payment intents, valid card information, and invalid merchants. 
    Their purpose is to provide a way to generate test data for domain-sepcific concepts.
*)

let defaultTestMerchants = [|
    { merchant_id = "grmklt123"; merchant_key = "1234567890" };
    { merchant_id = "hxeqnd852"; merchant_key = "1234567890" };
|]

type ValidMerchant = ValidMerchant of string with
    member x.Get = match x with ValidMerchant r -> r
    override x.ToString() = x.Get
    static member op_Explicit(ValidMerchant i) = i
    static member merchantExists =
        fun (ValidMerchant m) -> merchantExists m
    
    static member validMerchantArb () = 
        Gen.elements defaultTestMerchants |> Gen.map (fun m -> m.merchant_id)
        |> Arb.fromGen
        |> Arb.convert ValidMerchant ValidMerchant.op_Explicit
        |> Arb.filter ValidMerchant.merchantExists

type ValidPaymentIntent = ValidPaymentIntent of PaymentIntent with
    member x.Get = match x with ValidPaymentIntent r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(ValidPaymentIntent i) = i
    
    static member RandomPayments () =
        ArbMap.defaults |> ArbMap.arbitrary<PaymentIntent>
        |> Arb.convert ValidPaymentIntent ValidPaymentIntent.op_Explicit
        |> Arb.filter (fun p -> p.Get.amount > 0)

type ValidTimePeriod = ValidTimePeriod of uint32 with
    member x.Get = match x with ValidTimePeriod r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(ValidTimePeriod i) = i
    
    static member WithinTimeoutRange () =
        ArbMap.defaults |> ArbMap.arbitrary<uint32>
        |> Arb.convert ValidTimePeriod ValidTimePeriod.op_Explicit
        |> Arb.filter (fun p -> p.Get > 0u && int64 p.Get < CardAcceptTimeout)

type TimePeriodAfterTimeout = TimePeriodAfterTimeout of int64 with
    member x.Get = match x with TimePeriodAfterTimeout r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(TimePeriodAfterTimeout i) = i
    
    static member WithinTimeoutRange () =
        ArbMap.defaults |> ArbMap.arbitrary<uint32>
        |> Arb.filter (fun p -> p > 0u && int64 p < CardAcceptTimeout)
        |> Arb.convert (fun p -> int64 p + CardAcceptTimeout) (fun p -> uint32 (p - CardAcceptTimeout))
        |> Arb.convert TimePeriodAfterTimeout TimePeriodAfterTimeout.op_Explicit

let generateMonth = Gen.choose (1, 12)
let generateYear = Gen.choose (DateTime.Now.Year - 1 - 2000, DateTime.Now.Year + 3 - 2000)

let generateCCV = Gen.choose (0, 999)
let generateExpiryDate =
    Gen.map2 (fun m y -> $"%02d{m}/%02d{y}") generateMonth generateYear

let generateCardNumber =
    Gen.choose (0, 9999) |> Gen.four
    |> Gen.filter (fun (a,b,c,d) ->
        let checksum a = a / 1000 + (2 * (a % 1000 / 100)) % 9 + a % 100 / 10 + (2 * (a % 10)) % 9
        (checksum a + checksum b + checksum c + checksum d) % 10 = 0)
    |> Gen.map (fun (a,b,c,d) -> $"%04d{a}%04d{b}%04d{c}%04d{d}")
    

let generateCardInformation =
    Gen.map3 (fun e c n -> { card_number = n; card_holder_name = "ANDRII TESTER"; card_cvc = $"%03d{c}"; card_expiry = e })
        generateExpiryDate
        generateCCV
        generateCardNumber

type ValidCardInformation = ValidCardInformation of CardInformation with
    member x.Get = match x with ValidCardInformation r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(ValidCardInformation i) = i
    
    static member RandomCards () =
        Arb.fromGen generateCardInformation
        //ArbMap.defaults |> ArbMap.arbitrary<CardInformation>
        //|> Arb.fromGen generateCardInformation
        |> Arb.convert ValidCardInformation ValidCardInformation.op_Explicit
        |> Arb.filter (fun p -> validCard p.Get)

type InvalidMerchant = InvalidMerchant of string with
    member x.Get = match x with InvalidMerchant r -> r
    override x.ToString() = x.Get
    static member op_Explicit(InvalidMerchant i) = i