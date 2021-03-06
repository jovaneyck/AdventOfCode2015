open System
open System.Text.RegularExpressions

type RegisterName = string

type Sink =
    | RawValue of int
    | Register of RegisterName

type Command = 
    | NOP
    | Inc of register : Sink
    | Dec of register : Sink
    | Mul of left : Sink * right : Sink
    | Cpy of source : Sink * destination : Sink
    | Jnz of source : Sink * destination : Sink
    | Tgl of line : Sink

type State = {pointer : int; a : int; b : int; c : int; d : int}

let split (text : string) = text.Split('\n') |> List.ofArray |> List.map (fun s -> s.Trim())

let (|R|_|) pattern input =
    let m = Regex.Match(input, pattern)
    match m.Success with
    | false -> None
    | true -> Some <| List.skip 1 [for group in m.Groups -> group.Value]

let parseSink (sink : string) =
    match Int32.TryParse(sink) with
    | true, parsed -> RawValue parsed
    | false, _ -> Register sink
let (|Sink|_|) input = Some <| parseSink input

let parseLine line =
    match line with
    | R "NOP" [] -> NOP
    | R "inc (.*)" [Sink s] -> Inc s
    | R "dec (.*)" [Sink s] -> Dec s
    | R "cpy (.*) (.*)" [Sink src; Sink dest] -> Cpy (src, dest)
    | R "mul (.*) (.*)" [Sink left; Sink right] -> Mul (left, right)
    | R "jnz (.*) (.*)" [Sink src; Sink line] -> Jnz (src, line)
    | R "tgl (.*)" [Sink line] -> Tgl line
    | _ -> failwithf "Unknown command: %s" line

let parse input =
    input
    |> split
    |> List.map parseLine

let orDefault defaultValue value = defaultArg value defaultValue
let valueOf register state = 
    match register with
    | "a" -> state.a
    | "b" -> state.b
    | "c" -> state.c
    | "d" -> state.d
    | _ -> failwithf "Unknown register: %s" register
let update register value state = 
    match register with
    | "a" -> {state with a = value}
    | "b" -> {state with b = value}
    | "c" -> {state with c = value}
    | "d" -> {state with d = value}
    | _ -> failwithf "Unknown register: <%s>" register

let incrementPointer increment state =
    {state with pointer = state.pointer + increment}

let increment sink (state : State) = 
    (match sink with
    | RawValue v -> 
        printfn "Ignoring Inc because it's operating on raw value %d" v
        state
    | Register register ->
        let value = valueOf register state
        let newValue = value + 1
        state 
        |> update register newValue)
    |> incrementPointer 1

let decrement sink (state : State) = 
    (match sink with
    | RawValue v -> 
        printfn "Ignoring dec because it operates on raw value %d" v
        state
    | Register register ->
        let value = state |> valueOf register
        let newValue = value - 1
        state
        |> update register newValue)
    |> incrementPointer 1

let sinkValue state sink =
    match sink with
    | RawValue v -> v
    | Register r -> valueOf r state

let copy source destination state = 
    (match destination with
    | RawValue v ->
        printfn "Ignoring copy, as it tried to copy to a raw value %d instead of a register." v
        state
    | Register d ->
        let value = sinkValue state source
        state
        |> update d value)
    |> incrementPointer 1

let mul left right state = 
    let rightValue = sinkValue state right
    (match left with
    | RawValue v ->
        printfn "Ignoring mul, as it tried to multiply to a raw value %d instead of a register." v
        state
    | Register leftr ->
        let leftv = sinkValue state left
        let rightv = sinkValue state right
        let multiplication = leftv * rightv
        state
        |> update leftr multiplication
        |> update "c" 0
        |> update "d" 0)
    |> incrementPointer 1

let jnz state source destination =
    let value = sinkValue state source
    let offset = sinkValue state destination
    if value <> 0 then
        {state with pointer = state.pointer + offset}
    else
        {state with pointer = state.pointer + 1}

let withDefault v d = defaultArg d v
let toggle command =
    match command with
    | NOP -> failwithf "Tried to toggle a NOP. I don't think that's a good idea."
    | Inc r -> Dec r
    | Dec r -> Inc r
    | Tgl s -> Inc s
    | Jnz (s,l) -> Cpy (s,l)
    | Cpy (s,d) -> Jnz(s,d)
    | Mul (l,r) -> Jnz(l,r)

let replace program pointer newInstruction = 
    let (before, after) = program |> List.splitAt pointer
    before @ [newInstruction] @ (after |> List.tail)

let tgl state lineSink program = 
    let offset = sinkValue state lineSink
    let ptrToToggle = state.pointer + offset
    let instruction = program |> List.tryItem ptrToToggle
    instruction 
    |> Option.map toggle
    |> Option.map (replace program ptrToToggle)
    |> withDefault program


let runInstruction state instruction program = 
    match instruction with
    | NOP -> (state |> incrementPointer 1, program)
    | Inc reg -> (state |> increment reg, program)
    | Dec reg -> (state |> decrement reg, program)
    | Cpy (src, dest) -> (state |> copy src dest, program)
    | Mul (l,r) -> (state |> mul l r, program)
    | Jnz (src, line) -> (jnz state src line, program)
    | Tgl line -> (state |> incrementPointer 1, tgl state line program)

let rec run (state : State) program =
    let programLength = program |> List.length
    if (state.pointer > programLength - 1) then
        state
    else
        let instruction = program.[state.pointer]
//        printfn "Running instruction <%A>" instruction
//        printfn "%d %d %d %d" state.a state.b state.c state.d
        let (newState, newProgram) = runInstruction state instruction program
        run newState newProgram

open System.IO
#r "..\..\0 lib\F#\Unquote.dll"
open Swensen.Unquote

let testInput = @"cpy 2 a
tgl a
tgl a
tgl a
cpy 1 a
dec a
dec a"

testInput
|> parse
|> run {pointer = 0; a = 0; b = 0; c = 0; d = 0}

//Let's test the rewrite of the "multiply" instructionset to a single instruction:
let multiplyProgram = File.ReadAllText(System.IO.Path.Combine([| __SOURCE_DIRECTORY__; "multiply.txt"|]))
multiplyProgram |> parse |> run {pointer = 0; a = 3; b = 4; c = 1337; d = 1337} =! {pointer = 8; a = 12; b = 4; c = 0; d = 0}

let input = File.ReadAllText(System.IO.Path.Combine([| __SOURCE_DIRECTORY__; "input.txt"|]))
let rewrittenInput = File.ReadAllText(System.IO.Path.Combine([| __SOURCE_DIRECTORY__; "input_mult.txt"|]))

#time
let pt1 =
    input
    |> parse
    |> run {pointer = 0; a = 7; b = 0; c = 0; d = 0}

let pt2 =
    rewrittenInput
    |> parse
    |> run {pointer = 0; a = 12; b = 0; c = 0; d = 0}