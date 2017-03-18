﻿namespace ARM7TDMI

(* 
    High Level Programming @ Imperial College London # Spring 2017
    Project: A user-friendly ARM7TDMI assembler and simulator in F# and Web Technologies ( Github Electron & Fable Compliler )

    Contributors: Pranav Prabhu

    Module: Parser

    Description: Take in a Token List, Parse and return a List of InstrType or Error (and associated Error information)
    to AST for processing onwards to actual implementation. Parsing done using monadic parser combinators. 
    

    Sources: fsharpforfunandprofit.com, 
*)

module Parser =

    open System
    open Common
    open Toolkit

    type Pos = {
        lineNo : int
        tokenNo : int
    }

     type InitState = {
        lineList : List<Token>[]
        position : Pos
    }
    let initPos = {lineNo=0; tokenNo=0;}

    /// increment the tokenNo number
    let incrTok pos =  {pos with tokenNo=pos.tokenNo + 1}

    /// increment the lineNo number and set tokenNo to 0
    let incrLine pos = {lineNo=pos.lineNo + 1; tokenNo=0}

    // Create a new InitState from a Token List for a specific Line
    let tokenToInit tokenLst = 
        let y = splitBy TokNewLine tokenLst        
        match y with
            | [] -> {lineList = [||]; position = initPos;}
            | _ -> {lineList = List.toArray y; position=initPos;}

    // return the current line
    let currLine inputState = 
        let linePos = inputState.position.lineNo
        if linePos < inputState.lineList.Length then
            inputState.lineList.[linePos]
        else
            [TokEOF]

    /// Get the next token from the input, if any
    /// else return None. Also return the updated InputState
    /// Signature: InputState -> InputState * char option 
    let nextToken input =
        let linePos = input.position.lineNo
        let tokPos = input.position.tokenNo
        // three cases
        // 1) if line >= maxLine -> 
        //       return EOF
        // 2) if col less than line length -> 
        //       return char at colPos, increment colPos
        // 3) if col at line length -> 
        //       return NewLine, increment linePos

        if linePos >= input.lineList.Length then
            input, None
        else
            let currLine = currLine input
            if tokPos < currLine.Length then
                let token = currLine.[tokPos]
                let newPos = incrTok input.position 
                let newState = {input with position=newPos}
                newState, Some token
            else 
                // end of line, so return LF and move to next line
                let token = TokNewLine
                let newPos = incrLine input.position 
                let newState = {input with position=newPos}
                newState, Some token
    
    ///////// Inpit State required for tracking position errors /////////////
    type Input = InitState

    type PLabel = string
    type PError = string

    type PPosition = {
        currLine : List<Token>
        lineNo : int
        tokenNo : int
    }

    type Outcome<'a> =
        | Success of 'a
        | Failure of PLabel * PError * PPosition

    type Parser<'T> = {
        parseFunc : (InitState -> Outcome<'T * InitState>)
        pLabel:  PLabel
        }
        
    let parserPosfromInitState(initState:Input) = {
        currLine =  currLine initState
        lineNo = initState.position.lineNo
        tokenNo = initState.position.tokenNo
    }

    let printOutcome result =
        match result with
        | Success (value,input) -> 
            printfn "%A" value
        | Failure (label, err, parPos) -> 
            let errorLine = parPos.currLine
            let tokPos = parPos.tokenNo
            let linePos = parPos.lineNo
            let failureLine = sprintf "%*s^%s" tokPos "" err
            printfn "Line:%i - TokenNo:%i Error parsing %A\n %A\n %s" linePos tokPos label errorLine failureLine
    //convert Error Line to String....
    let rec readAllTokens input =
        [
            let remainingInput,tokenOpt = nextToken input 
            match tokenOpt with
            | None -> 
                // end of input
                ()
            | Some tk -> 
                // return first character
                yield tk
                // return the remaining characters
                yield! readAllTokens remainingInput
        ]

    let setLabel parser newLabel = 
        // change the inner function to use the new label
        let newInnerFn input = 
            let result = parser.parseFunc input
            match result with
            | Success s ->
                // if Success, do nothing
                Success s 
            | Failure (oldLabel,err, parPos) -> 
                // if Failure, return new label
                Failure (newLabel,err, parPos)        // <====== use newLabel here
        // return the Parser
        {parseFunc=newInnerFn; pLabel=newLabel}


    let ( <?> ) = setLabel

    let satisfy predicate label =
        let innerFn tokenLst=
            let remainInput, tokenOpt = nextToken tokenLst
            match tokenOpt with 
                | None -> let err = "No more input"
                          let pos = parserPosfromInitState tokenLst
                          Failure (label,err,pos) 
                | Some first -> 
                    if predicate first then
                        Success (first,remainInput)
                    else
                        let err = sprintf "Unexpected '%A'" first
                        let pos = parserPosfromInitState tokenLst
                        Failure (label,err,pos)
        // return the parser
        {parseFunc=innerFn; pLabel=label}

    // TODO: Split this up into important subtoken structures
    let pToken tokenToMatch = 
        let predicate tk = (tk = tokenToMatch) 
        let label = sprintf "%A" tokenToMatch 
        satisfy predicate label 

    let runInput parser input = 
        parser.parseFunc input
    /// Run a parser with some input

    let run parser inputTokenLst = 
        runInput parser (tokenToInit inputTokenLst)

    /// "bindP" takes a parser-producing function f, and a parser p
    /// and passes the output of p into f, to create a new parser
    let bindP f p =
        let label = "Empty"
        let innerFn input =
            let res1 = runInput p input 
            match res1 with
            | Failure (label, err, pos) -> 
                // return error from parser1
                Failure (label, err, pos) 
            | Success (value1,remainingInput) ->
                // apply f to get a new parser
                let p2 = f value1
                // run parser with remaining input
                runInput p2 remainingInput
        {parseFunc =innerFn; pLabel=label }

    /// Infix version of bindP
    let ( >>= ) p f = bindP f p

    /// Lift a value to a Parser
    let returnP x = 
        let innerFn input =
            // ignore the input and return x
            Success (x,input)
        // return the inner function
        {parseFunc =innerFn; pLabel= "Success"} 

    /// apply a function to the value inside a parser
    let mapP f = 
        bindP (f >> returnP)

    /// infix version of mapP


    /// "piping" version of mapP
    let ( |>> ) x f = mapP f x

    /// apply a wrapped function to a wrapped value
    let applyP fP xP =         
        fP >>= (fun f -> xP >>= (fun x -> returnP (f x) ))

    /// infix version of apply
    let ( <*> ) = applyP

    /// lift a two parameter function to Parser World
    let lift2 f xP yP =
        returnP f <*> xP <*> yP

    /// Combine two parsers as "A andThen B"
    let andThen p1 p2 =   
        let label = sprintf "%A andThen %A" (setLabel p1) (setLabel p2)      
        p1 >>= (fun p1Result -> 
        p2 >>= (fun p2Result -> 
            returnP (p1Result,p2Result) ))
        <?> label

    /// Infix version of andThen
    let ( .>>. ) = andThen

    /// Combine two parsers as "A orElse B"
    let orElse p1 p2 =
        let innerFn input =
            // run parser1 with the input
            let result1 = runInput p1 input

            // test the result for Failure/Success
            match result1 with
                | Success result -> 
                // if success, return the original result
                    result1

                | Failure (label, err, pos) -> 
                // if failed, run parser2 with the input
                    let result2 = runInput p2 input

                // return parser2's result
                    result2 

        // return the inner function
        {parseFunc =innerFn; pLabel = p1.pLabel;}

    /// Infix version of orElse
    let ( <|> ) = orElse

    /// Choose any of a list of parsers
    let choice listOfParsers = 
        List.reduce ( <|> ) listOfParsers 

    /// Choose any of a list of characters
    let anyOf tokenList = 
        let label = sprintf "anyOf %A" tokenList
        tokenList
        |> List.map pToken // convert into parsers
        |> choice
        <?> label
    /// Convert a list of Parsers into a Parser of a list
    let rec sequence parserList =
        // define the "cons" function, which is a two parameter function
        let cons head tail = head::tail

        // lift it to Parser World
        let consP = lift2 cons

        // process the list of parsers recursively
        match parserList with
        | [] -> 
            returnP []
        | h::tl ->
            consP h (sequence tl)
    /// Parses an optional occurrence of p and returns an option value.
    let opt p = 
        let some = p |>> Some
        let none = returnP None
        some <|> none

    /// Keep only the result of the left side parser
    let (.>>) p1 p2 = 
        // create a pair
        p1 .>>. p2 
        // then only keep the first value
        |> mapP (fun (a,b) -> a) 

    /// Keep only the result of the right side parser
    let (>>.) p1 p2 = 
        // create a pair
        p1 .>>. p2 
        // then only keep the second value
        |> mapP (fun (a,b) -> b) 

    /// Keep only the result of the middle parser
    let between p1 p2 p3 = 
        p1 >>. p2 .>> p3 



    let (>>%) p x =
        p |>> (fun _ -> x)

    let createParserForwardedToRef<'a>() =

        let dummyParser= 
            let innerFn input : Outcome<'a * Input> = failwith "unfixed forwarded parser"
            {parseFunc=innerFn; pLabel="Unknown"}
        
        // ref to placeholder Parser
        let parserRef = ref dummyParser 

        // wrapper Parser
        let innerFn input = 
            // forward input to the placeholder
            runInput !parserRef input 
        let outParser = {parseFunc=innerFn; pLabel="Unknown"}

        outParser, parserRef

    let parseInstr,parseInstrForRef = createParserForwardedToRef<Instr>()

    /////////////////////////////////// Object Lists TO BE DISCARDED DUE TO FABLE NOT WORKING WITH ENUM ////////////////////////////////////    
    let tokenCondList = [TokCond(EQ); TokCond(NE); TokCond(CS); TokCond(HS); TokCond(CC); TokCond(LO); TokCond(MI); TokCond(PL);
                         TokCond(VS); TokCond(VC); TokCond(HI); TokCond(LS); TokCond(GE); TokCond(LT); TokCond(GT); TokCond(LE);
                         TokCond(AL); TokCond(NV);]
    let regList = [R0  ; R1  ; R2  ; R3  ; R4
                ; R5  ; R6  ; R7  ; R8  ; R9
                ; R10 ; R11 ; R12 ; R13 ; R14
                ; R15;]

    let tokenRegList = List.map TokReg regList
    let tokenInstrList1 = [TokInstr1(MOV); TokInstr1(MVN)]
    let tokenInstrList2 = [TokInstr2(ADR)]
    let instrList3 = [ADD ; ADC ; SUB ; SBC ; RSB ; RSC ; AND ; EOR ; BIC ; ORR;]
    let tokeninstrList3 = List.map TokInstr3 instrList3

    let instrList4 = [LSL ; LSR ; ASR ; ROR_;]
    let tokenInstrList4 = List.map TokInstr4 instrList4
    let tokenInstrList5 = [TokInstr5(RRX_)]
    let instrList6 = [CMP ; CMN ; TST ; TEQ;]
    let tokenInstrList6 = List.map TokInstr6 instrList6

    let tokenInstrList7 = [TokInstr7(LDR); TokInstr7(STR);] 
    let tokenInstrList8 = [TokInstr8(LDM); TokInstr8(STM);]

    let tokenInstrList9 = [TokInstr9(BL);]

    let pInstr1 = 
        let parseTuple = anyOf tokenInstrList1 <?> "Type 1 Opcode"
        let tupleTransform(c1) =
            match c1 with 
            | TokInstr1 a -> a
            | _ -> failwith "Impossible"
        mapP tupleTransform parseTuple
    let pInstr2 = anyOf tokenInstrList2 
    let pInstr3 = anyOf tokeninstrList3

    let pInstr4 = anyOf tokenInstrList4

    let pInstr5 = anyOf tokenInstrList5 
    let pInstr6 = anyOf tokenInstrList6 
    let pInstr7 = anyOf tokenInstrList7 
    let pInstr8 = anyOf tokenInstrList8 
    let pInstr9 = anyOf tokenInstrList9 
    let pS = 
        let parseTuple = pToken (TokS S) <?> "S Type"
        let tupleTransform(c1) =
            match c1 with 
            | TokS a -> a
            | _ -> failwith "Impossible"
        mapP tupleTransform parseTuple
    let pComma = 
        let parseTuple = pToken TokComma <?> "Comma"
        let tupleTransform(c1) =
            match c1 with 
            | TokComma -> TokComma
            | _ -> failwith "Impossible"
        mapP tupleTransform parseTuple

    let pCond = 
        let parseTuple = anyOf tokenCondList <?> "Conditional Code"
        let tupleTransform(c1) =
            match c1 with 
            | TokCond a -> a 
            | _ -> failwith "Impossible"
        mapP tupleTransform parseTuple
    let pReg = 
        let parseTuple = anyOf tokenRegList <?> "Register"
        let tupleTransform(c1) =
            match c1 with 
            | TokReg a -> a 
            | _ -> failwith "Impossible"
        mapP tupleTransform parseTuple
    let pRegComma = 
            let parseTuple = pReg .>>. pComma <?> "Reg followed by Comma"
            let tupleTransform (c1,c2) = 
                match c1, c2 with  
                | a, TokComma-> a
                | _ -> failwith "Impossible"
            mapP tupleTransform parseTuple

    let pLiteral = pToken (TokLiteral 10)
    let pInput = pComma .>>. pReg <?> "Input Type"
    let pOp = pInput .>>. pInstr4 <?> "Operand"

    let instType1 = 
        let label = "Instruction Type 1"
        let tupleTransform = function
            | x -> JInstr1(x)

    //   let tupleTransform (((((t1,t2), t3),t4),t5),t6) = 
    //       match t1,t2,t3,t4,t5,t6 with 
    //       | TokInstr1 a, TokS b, TokCond c, TokReg d, TokComma, TokReg e -> JInstr1

        let instr1Hold = pInstr1 .>>. opt pS .>>. opt pCond .>>. pRegComma .>>. pReg <?> label
        mapP tupleTransform instr1Hold

    (*
    let instType2 = 
        let label = "Instruction Type 2"
        pInstr2 .>>. opt pS .>>. opt pCond .>>. pReg  |>> TInstr2 <?> label

    let instType3 =
        let label = "Instruction Type 3"
        pInstr3 .>>. opt pS .>>. opt pCond .>>. pRegComma .>>. pReg .>>. opt pOp  |>> TInstr3 <?> label

    let instType4 = 
        let label = "Instruction Type 4"
        (pInstr4 .>>. opt pS .>>. opt pCond .>>. pRegComma .>>. pReg |>> TInstr4 <?> label

    let instType5 = 
        let pInstr4 = anyOf tokenInstrList5 <?> "Type 5 Opcode"
        0

    let label = "Instruction Type 5"
        (pInstr4 .>>. opt pS .>>. opt pCond .>>. pReg >>% JInstr5) <?> label


    parseInstr := choice 
        [
        instType1
        instType2
        instType3
        ]
    *)

    //////////////////Testing//////////////

    let testInstrType1List1 = [TokInstr1(MOV); TokReg(R0); TokReg(R1)]
    (*
    let testInstrType1List2 = [TokInstr1(MOV); TokInput(ID(R0)); TokInput(Literal(10));]

    let testInstrType1List3 = [TokInstr1(MVN); TokS(S); TokInput(ID(R0)); TokInput(ID(R1))]

    let testInstrType1List4 = [TokInstr1(MVN); TokS(S); TokCond(EQ); TokInput(ID(R0)); TokInput(Literal(10))]

    let testInstrType1ListFail1 = [TokInstr1(MVN); TokError("R20"); TokInput(Literal(10))]
    let testInstrType1ListFail2 = [TokInstr1(MVN); TokError("R16"); TokError("R20");]

    let testInstrType1ListFail3 = [TokInstr1(MOV); TokError("B"); TokInput(ID(R0)); TokInput(ID(R1))]

    let testInstrType1ListFail4 = [TokInstr1(MOV); TokS(S); TokError("ER"); TokInput(ID(R0)); TokInput(Literal(10))]

    *)

    let t1 = [TokReg(R0); TokError("1$");]

    printf "%A" (run pRegComma t1)
