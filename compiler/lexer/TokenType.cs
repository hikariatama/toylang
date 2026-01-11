// Inspired by https://github.com/mikey-b/Compiler-Techniques/blob/main/C%2B%2B-Dynamic%20Type%20Test/dyntest.cpp#L6-L22
public enum TokenType
{
    Parens = 2, // ()
    ParensLeft = Parens * 3, // (
    ParensRight = Parens * 5, // )
    Dot = 7, // .
    Bracket = 11, // []
    BracketLeft = Bracket * 13, // [
    BracketRight = Bracket * 17, // ]
    Colon = 19, // :
    Comma = 23, // ,
    ColonEqual = 29, // :=
    EqualGreater = 31, // =>
    Class = 37, // class
    Extends = 41, // extends
    Is = 43, // is
    End = 47, // end
    Var = 53, // var
    Method = 59, // method
    While = 61, // while
    Loop = 67, // loop
    If = 71, // if
    Then = 73, // then
    Else = 79, // else
    Elif = 181, // elif
    Return = 83, // return
    This = 89, // this
    Identifier = 97, // identifier
    Literal = 101, // literal
    Integer = Literal * 103, // integer
    Real = Literal * 107, // real
    Boolean = Literal * 109, // boolean
    True = Boolean * 113, // true
    False = Boolean * 127, // false
    String = Literal * 151, // string
    Eof = 131, // end of file
    LineComment = 137, // //
    Break = 149, // break
}
