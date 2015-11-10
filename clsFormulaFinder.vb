﻿Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports MwtWinDll.MolecularWeightCalculator

Public Class clsFormulaFinder

#Region "Constants"
    Private Const MAX_MATCHINGELEMENTS = 10
    Public Const DEFAULT_RESULTS_TO_FIND = 100
    Public Const MAXIMUM_ALLOWED_RESULTS_TO_FIND = 100000

    Public Const MAX_BOUNDED_SEARCH_COUNT = 65565
#End Region

#Region "Strutures and Enums"

    ''' <summary>
    ''' Search tolerances for each element
    ''' </summary>
    ''' <remarks>
    ''' Target percent composition values must be between 0 and 100; they are only used when calling FindMatchesByPercentComposition
    ''' MinimumCount and MaximumCount are only used when the search mode is Bounded; they are ignored for Thorough search
    ''' </remarks>
    Public Structure udtCandidateElementTolerances
        Public TargetPercentComposition As Double
        Public MinimumCount As Integer
        Public MaximumCount As Integer
    End Structure

    Public Structure udtFormulaFinderMassResult
        Public EmpiricalFormula As String
        Public CountsByElement As Dictionary(Of String, Integer)
        Public Mass As Double
        Public MassError As Double
        Public DeltaMass As Double
        Public DeltaMassIsPPM As Boolean
        Public MZ As Double

        Public ChargeState As Integer

        ''' <summary>
        ''' Percent composition results (only valid if matching percent compositions)
        ''' </summary>
        ''' <remarks>Keys are element or abbreviation symbols, values are percent composition, between 0 and 100</remarks>
        Public PercentComposition As Dictionary(Of String, Double)

        Public SortKey As String
    End Structure
    
    Private Structure udtElementNumType
        Public H As Integer
        Public C As Integer
        Public Si As Integer
        Public N As Integer
        Public P As Integer
        Public O As Integer
        Public S As Integer
        Public Cl As Integer
        Public I As Integer
        Public F As Integer
        Public Br As Integer
        Public Other As Integer
    End Structure
    
    Private Enum eCalculationMode
        MatchMolecularWeight = 0
        MatchPercentComposition = 1
    End Enum

    Public Enum eSearchMode
        Thorough = 0
        Bounded = 1
    End Enum
#End Region

#Region "Member Variables"

    Private mAbortProcessing As Boolean
    Private mCalculating As Boolean
    Private mErrorMessage As String

    ''' <summary>
    ''' Keys are element symbols, abbreviations, or even simply a mass value
    ''' Values are target percent composition values, between 0 and 100
    ''' </summary>
    ''' <remarks>The target percent composition values are only used when FindMatchesByPercentComposition is called</remarks>
    Private mCandidateElements As Dictionary(Of String, udtCandidateElementTolerances)

    Private ReadOnly mElementAndMassRoutines As MWElementAndMassRoutines

    Private mMaximumHits As Integer

    Private mRecursiveCount As Integer
    Private mRecursiveFunctionCallCount As Integer
    Private mMaxRecursiveCount As Integer

    ''' <summary>
    ''' Percent complete, between 0 and 100
    ''' </summary>
    ''' <remarks></remarks>
    Private mPercentComplete As Double

#End Region

#Region "Properties"

    ''' <summary>
    ''' Element symbols to consider when finding empirical formulas
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks>The values in the dictionary are target percent composition values; only used if you call FindMatchesByPercentComposition</remarks>
    Public Property CandidateElements As Dictionary(Of String, udtCandidateElementTolerances)
        Get
            Return mCandidateElements
        End Get
        Set(value As Dictionary(Of String, udtCandidateElementTolerances))
            If Not value Is Nothing Then
                mCandidateElements = value

                ValidateBoundedSearchValues()
                ValidatePercentCompositionValues()
            End If
        End Set
    End Property

    Public Property EchoMessagesToConsole As Boolean

    Public Property MaximumHits As Integer
        Get
            Return mMaximumHits
        End Get
        Set(value As Integer)
            If value < 1 Then
                value = 1
            End If

            If value > MAXIMUM_ALLOWED_RESULTS_TO_FIND Then
                value = MAXIMUM_ALLOWED_RESULTS_TO_FIND
            End If

            mMaximumHits = value
        End Set
    End Property

    ''' <summary>
    ''' Percent complete, between 0 and 100
    ''' </summary>
    ''' <remarks></remarks>
    Public ReadOnly Property PercentComplete As Double
        Get
            Return mPercentComplete
        End Get
    End Property

    Public Property SearchMode As eSearchMode

    Public Property SortResults As Boolean

    Public Property VerifyHydrogens As Boolean
#End Region

#Region "Events"
    Public Event MessageEvent(strMessage As String)
    Public Event ErrorEvent(strErrorMessage As String)
    Public Event WarningEvent(strWarningMessage As String)
#End Region

    ''' <summary>
    ''' Constructor
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub New(oMWElementAndMassRoutines As MWElementAndMassRoutines)

        mElementAndMassRoutines = oMWElementAndMassRoutines
        mCandidateElements = New Dictionary(Of String, udtCandidateElementTolerances)

        EchoMessagesToConsole = True

        Reset()

    End Sub

#Region "Public Methods"

    Public Sub AbortProcessingNow()
        mAbortProcessing = True
    End Sub

    Public Sub AddCandidateElement(elementSymbolAbbrevOrMass As String)

        Dim udtElementTolerances = GetDefaultCandidateElementTolerance()

        AddCandidateElement(elementSymbolAbbrevOrMass, udtElementTolerances)
    End Sub

    Public Sub AddCandidateElement(elementSymbolAbbrevOrMass As String, udtElementTolerances As udtCandidateElementTolerances)

        If mCandidateElements.ContainsKey(elementSymbolAbbrevOrMass) Then
            mCandidateElements(elementSymbolAbbrevOrMass) = udtElementTolerances
        Else
            mCandidateElements.Add(elementSymbolAbbrevOrMass, udtElementTolerances)
        End If
    End Sub

    Public Function FindMatchesByMassPPM(targetMass As Double, massTolerancePPM As Double, massSearchOptions As clsFormulaFinderOptions) As List(Of udtFormulaFinderMassResult)
        Dim massToleranceDa = massTolerancePPM * targetMass / 1000000.0
        If massSearchOptions Is Nothing Then massSearchOptions = New clsFormulaFinderOptions()

        Dim lsResults = FindMatchesByMass(targetMass, massToleranceDa, massSearchOptions, True)
        Return lsResults

    End Function

    Public Function FindMatchesByMass(targetMass As Double, massToleranceDa As Double, massSearchOptions As clsFormulaFinderOptions) As List(Of udtFormulaFinderMassResult)
        If massSearchOptions Is Nothing Then massSearchOptions = New clsFormulaFinderOptions()

        Dim lsResults = FindMatchesByMass(targetMass, massToleranceDa, massSearchOptions, False)
        Return lsResults

    End Function

    Public Function FindMatchesByPercentComposition(
     maximumFormulaMass As Double,
     percentTolerance As Double,
     massSearchOptions As clsFormulaFinderOptions) As List(Of udtFormulaFinderMassResult)

        If massSearchOptions Is Nothing Then massSearchOptions = New clsFormulaFinderOptions()

        Dim lsResults = FindMatchesByPercentCompositionWork(maximumFormulaMass, percentTolerance, massSearchOptions)
        Return lsResults

    End Function
    
    ''' <summary>
    ''' Reset to defaults
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub Reset()

        mCandidateElements.Clear()
        mCandidateElements.Add("C", GetDefaultCandidateElementTolerance(70))
        mCandidateElements.Add("H", GetDefaultCandidateElementTolerance(10))
        mCandidateElements.Add("N", GetDefaultCandidateElementTolerance(10))
        mCandidateElements.Add("O", GetDefaultCandidateElementTolerance(10))

        mErrorMessage = String.Empty
        mAbortProcessing = False

        MaximumHits = DEFAULT_RESULTS_TO_FIND
        SortResults = True

    End Sub

#End Region

    Private Sub AppendToEmpiricalFormula(sbEmpiricalFormula As StringBuilder, elementSymbol As String, elementCount As Integer)
        If elementCount <> 0 Then
            sbEmpiricalFormula.Append(elementSymbol)

            If elementCount > 1 Then
                sbEmpiricalFormula.Append(elementCount)
            End If
        End If

    End Sub

    Private Sub AppendPercentCompositionResult(
       udtSearchResult As udtFormulaFinderMassResult,
       elementcount As Integer,
       elementSymbol As String,
       percentComposition As Double)

        If elementcount <> 0 Then
            udtSearchResult.PercentComposition.Add(elementSymbol, percentComposition)
        End If

    End Sub


    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="targetMass">Only used when calculationMode is MatchMolecularWeight</param>
    ''' <param name="massToleranceDa">Only used when calculationMode is MatchMolecularWeigh</param>
    ''' <param name="maximumFormulaMass">Only used when calculationMode is MatchPercentComposition</param>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="ppmMode"></param>
    ''' <param name="calculationMode"></param>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="dblPotentialElementStats"></param>
    ''' <param name="strPotentialElements"></param>
    ''' <param name="dblTargetPercents"></param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function BoundedSearch(
       targetMass As Double,
       massToleranceDa As Double,
       maximumFormulaMass As Double,
       massSearchOptions As clsFormulaFinderOptions,
       ppmMode As Boolean,
       calculationMode As eCalculationMode,
       potentialElementCount As Integer,
       ByVal dblPotentialElementStats(,) As Double,
       ByVal strPotentialElements() As String,
       ByVal intRange(,) As Integer,
       ByVal dblTargetPercents(,) As Double) As List(Of udtFormulaFinderMassResult)

        Dim lstResults As List(Of udtFormulaFinderMassResult)

        If massSearchOptions.FindTargetMZ Then
            ' Searching for target m/z rather than target mass

            MultipleSearchMath(potentialElementCount, massSearchOptions)

            lstResults = OldFormulaFinder(massSearchOptions, ppmMode, calculationMode, strPotentialElements, dblPotentialElementStats, potentialElementCount, targetMass, massToleranceDa, maximumFormulaMass, intRange, dblTargetPercents)
        Else
            massSearchOptions.ChargeMin = 1
            massSearchOptions.ChargeMax = 1

            lstResults = OldFormulaFinder(massSearchOptions, ppmMode, calculationMode, strPotentialElements, dblPotentialElementStats, potentialElementCount, targetMass, massToleranceDa, maximumFormulaMass, intRange, dblTargetPercents)
        End If

        ComputeSortKeys(lstResults)

        Return lstResults

    End Function

    Private Sub ComputeSortKeys(lstResults As IEnumerable(Of udtFormulaFinderMassResult))

        ' Compute the sort key for each result
        Dim sbCodeString = New StringBuilder()

        For Each item In lstResults
            item.SortKey = ComputeSortKey(sbCodeString, item.EmpiricalFormula)
        Next
    End Sub

    Private Function ComputeSortKey(sbCodeString As StringBuilder, empiricalFormula As String) As String

        ' Precedence order for sbCodeString
        '  C1_ C2_ C3_ C4_ C5_ C6_ C7_ C8_ C9_  a   z    1,  2,  3...
        '   1   2   3   4   5   6   7   8   9   10  35   36  37  38
        '
        ' Custom elements are converted to Chr(1), Chr(2), etc.
        ' Letters are converted to Chr(10) through Chr(35)
        ' Number are converted to Chr(36) through Chr(255)
        '
        ' 220 = Chr(0) + Chr(220+35) = Chr(0) + Chr(255)

        ' 221 = Chr(CInt(Math.Floor(221+34/255))) + Chr((221 + 34) Mod 255 + 1)

        Dim charIndex = 0
        Dim formulaLength = empiricalFormula.Length
        Dim parsedValue As Integer

        sbCodeString.Clear()

        While charIndex < formulaLength
            Dim strCurrentLetter = Char.ToUpper(empiricalFormula(charIndex))

            If (Char.IsLetter(strCurrentLetter)) Then

                sbCodeString.Append(Chr(0))

                If charIndex + 2 < formulaLength AndAlso empiricalFormula.Substring(charIndex + 2, 1) = "_" Then
                    ' At a custom element, which are notated as "C1_", "C2_", etc.
                    ' Give it a value of Chr(1) through Chr(10)
                    ' Also, need to bump up charIndex by 2

                    Dim customElementNum = empiricalFormula.Substring(charIndex + 1, 1)

                    If Integer.TryParse(customElementNum, parsedValue) Then
                        sbCodeString.Append(Chr(parsedValue))
                    Else
                        sbCodeString.Append(Chr(1))
                    End If

                    charIndex += 2
                Else
                    ' 65 is the ascii code for the letter a
                    ' Thus, 65-55 = 10
                    Dim asciiValue = Asc(strCurrentLetter)
                    sbCodeString.Append(Chr(asciiValue - 55))

                End If
            ElseIf strCurrentLetter <> "_" Then
                ' At a number, since empirical formulas can only have letters or numbers or underscores

                Dim endIndex = charIndex
                While endIndex + 1 < formulaLength
                    Dim nextChar = empiricalFormula(endIndex + 1)
                    If Not Integer.TryParse(nextChar, parsedValue) Then
                        Exit While
                    End If
                    endIndex += 1
                End While

                If Integer.TryParse(empiricalFormula.Substring(charIndex, endIndex - charIndex + 1), parsedValue) Then
                    If parsedValue < 221 Then
                        sbCodeString.Append(Chr(0))
                        sbCodeString.Append(Chr(parsedValue + 35))
                    Else
                        sbCodeString.Append(Chr(CInt(Math.Floor(parsedValue + 34 / 255))))
                        sbCodeString.Append(Chr((parsedValue + 34) Mod 255 + 1))
                    End If
                End If

                charIndex = endIndex
            End If

            charIndex += 1

        End While

        Return sbCodeString.ToString()

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="totalMass"></param>
    ''' <param name="totalCharge"></param>
    ''' <param name="targetMass"></param>
    ''' <param name="massToleranceDa"></param>
    ''' <param name="intMultipleMtoZCharge"></param>
    ''' <remarks>True if the m/z is within tolerance of the target</remarks>
    Private Function CheckMtoZWithTarget(totalMass As Double, totalCharge As Double, targetMass As Double, massToleranceDa As Double, intMultipleMtoZCharge As Integer) As Boolean
        Dim dblMtoZ As Double, dblOriginalMtoZ As Double

        ' The original target is the target m/z; assure this compound has that m/z
        If Math.Abs(totalCharge) > 0.5 Then
            dblMtoZ = Math.Abs(totalMass / totalCharge)
        Else
            dblMtoZ = 0
        End If

        If intMultipleMtoZCharge = 0 Then
            Return False
        End If

        dblOriginalMtoZ = targetMass / intMultipleMtoZCharge
        If dblMtoZ < dblOriginalMtoZ - massToleranceDa Or dblMtoZ > dblOriginalMtoZ + massToleranceDa Then
            ' dblMtoZ is not within tolerance of dblOriginalMtoZ, so don't report the result
            Return False
        End If

        Return True

    End Function

    Private Function Combinatorial(a As Integer, B As Integer) As Double
        If a > 170 Or B > 170 Then
            Console.WriteLine("Cannot compute factorial of a number over 170.  Thus, cannot compute the combination.")
            Return -1
        ElseIf a < B Then
            Console.WriteLine("First number should be greater than or equal to the second number")
            Return -1
        Else
            Return Factorial(a) / (Factorial(B) * Factorial(a - B))
        End If
    End Function

    ''' <summary>
    ''' Construct the empirical formula and verify hydrogens
    ''' </summary>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="sbEmpiricalFormula"></param>
    ''' <param name="count1"></param>
    ''' <param name="count2"></param>
    ''' <param name="count3"></param>
    ''' <param name="count4"></param>
    ''' <param name="count5"></param>
    ''' <param name="count6"></param>
    ''' <param name="count7"></param>
    ''' <param name="count8"></param>
    ''' <param name="count9"></param>
    ''' <param name="count10"></param>
    ''' <param name="elem1"></param>
    ''' <param name="elem2"></param>
    ''' <param name="elem3"></param>
    ''' <param name="elem4"></param>
    ''' <param name="elem5"></param>
    ''' <param name="elem6"></param>
    ''' <param name="elem7"></param>
    ''' <param name="elem8"></param>
    ''' <param name="elem9"></param>
    ''' <param name="elem10"></param>
    ''' <param name="totalMass"></param>
    ''' <param name="targetMass">Only used when massSearchOptions.FindTargetMZ is true, and that is only valid when matching a target mass, not when matching percent composition values</param>
    ''' <param name="massToleranceDa">Only used when massSearchOptions.FindTargetMZ is true</param>
    ''' <param name="totalCharge"></param>
    ''' <param name="intMultipleMtoZCharge">When massSearchOptions.FindTargetMZ is false, this will be 1; otherwise, the current charge being searched for</param>
    ''' <returns>False if compound has too many hydrogens AND hydrogen checking is on, otherwise returns true</returns>
    ''' <remarks>Common function to both molecular weight and percent composition matching</remarks>
    Private Function ConstructAndVerifyCompound(
       massSearchOptions As clsFormulaFinderOptions,
       sbEmpiricalFormula As StringBuilder,
       count1 As Integer, count2 As Integer, count3 As Integer, count4 As Integer, count5 As Integer, count6 As Integer, count7 As Integer, count8 As Integer, count9 As Integer, count10 As Integer,
       elem1$, elem2$, elem3$, elem4$, elem5$, elem6$, elem7$, elem8$, elem9$, elem10$,
       totalMass As Double,
       targetMass As Double,
       massToleranceDa As Double,
       totalCharge As Double,
       intMultipleMtoZCharge As Integer) As Boolean

        sbEmpiricalFormula.Clear()

        Try
            ' This dictionary tracks the elements and abbreviations of the found formula so that they can be properly ordered according to empirical formula conventions
            ' Key is the element or abbreviation symbol, value is the number of each element or abbreviation
            Dim empiricalResultSymbols As New Dictionary(Of String, Integer)

            ' Convert to empirical formula and sort
            If count1 <> 0 Then empiricalResultSymbols.Add(elem1$, count1)
            If count2 <> 0 Then empiricalResultSymbols.Add(elem2$, count2)
            If count3 <> 0 Then empiricalResultSymbols.Add(elem3$, count3)
            If count4 <> 0 Then empiricalResultSymbols.Add(elem4$, count4)
            If count5 <> 0 Then empiricalResultSymbols.Add(elem5$, count5)
            If count6 <> 0 Then empiricalResultSymbols.Add(elem6$, count6)
            If count7 <> 0 Then empiricalResultSymbols.Add(elem7$, count7)
            If count8 <> 0 Then empiricalResultSymbols.Add(elem8$, count8)
            If count9 <> 0 Then empiricalResultSymbols.Add(elem9$, count9)
            If count10 <> 0 Then empiricalResultSymbols.Add(elem10$, count10)

            Dim valid = ConstructAndVerifyCompoundWork(massSearchOptions, sbEmpiricalFormula, totalMass, targetMass, massToleranceDa, totalCharge, intMultipleMtoZCharge, empiricalResultSymbols)
            Return valid

        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("ConstructAndVerifyCompound", 0, ex.Message)
            Return False
        End Try

    End Function

    ''' <summary>
    ''' Construct the empirical formula and verify hydrogens
    ''' </summary>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="sbEmpiricalFormula"></param>
    ''' <param name="strPotentialElements"></param>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="lstNewPotentialElementPointers"></param>
    ''' <param name="totalMass"></param>
    ''' <param name="targetMass">Only used when massSearchOptions.FindTargetMZ is true, and that is only valid when matching a target mass, not when matching percent composition values</param>
    ''' <param name="massToleranceDa">Only used when massSearchOptions.FindTargetMZ is true</param>
    ''' <param name="totalCharge"></param>
    ''' <param name="intMultipleMtoZCharge">When massSearchOptions.FindTargetMZ is false, this will be 0; otherwise, the current charge being searched for</param>
    ''' <returns>False if compound has too many hydrogens AND hydrogen checking is on, otherwise returns true</returns>
    ''' <remarks>Common function to both molecular weight and percent composition matching</remarks>
    Private Function ConstructAndVerifyCompoundRecursive(
       massSearchOptions As clsFormulaFinderOptions,
       sbEmpiricalFormula As StringBuilder,
       strPotentialElements() As String,
       potentialElementCount As Integer,
       lstNewPotentialElementPointers As List(Of Integer),
       totalMass As Double,
       targetMass As Double,
       massToleranceDa As Double,
       totalCharge As Double,
       intMultipleMtoZCharge As Integer) As Boolean

        sbEmpiricalFormula.Clear()

        Try
            Dim elementCountArray = GetElementCountArray(potentialElementCount, lstNewPotentialElementPointers)

            ' This dictionary tracks the elements and abbreviations of the found formula so that they can be properly ordered according to empirical formula conventions
            ' Key is the element or abbreviation symbol, value is the number of each element or abbreviation
            Dim empiricalResultSymbols As New Dictionary(Of String, Integer)

            For intIndex = 0 To potentialElementCount - 1
                If elementCountArray(intIndex) <> 0 Then
                    empiricalResultSymbols.Add(strPotentialElements(intIndex), elementCountArray(intIndex))
                End If
            Next intIndex

            Dim valid = ConstructAndVerifyCompoundWork(massSearchOptions, sbEmpiricalFormula, totalMass, targetMass, massToleranceDa, totalCharge, intMultipleMtoZCharge, empiricalResultSymbols)
            Return valid

        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("ConstructAndVerifyCompoundRecursive", 0, ex.Message)
            Return False
        End Try

    End Function

    Private Function GetElementCountArray(
       potentialElementCount As Integer,
       lstNewPotentialElementPointers As IEnumerable(Of Integer)) As Integer()

        ' Store the occurrence count of each element
        Dim elementCountArray(potentialElementCount) As Integer

        For Each elementIndex In lstNewPotentialElementPointers
            elementCountArray(elementIndex) += 1
        Next

        Return elementCountArray

    End Function

    Private Function ConstructAndVerifyCompoundWork(
       massSearchOptions As clsFormulaFinderOptions,
       sbEmpiricalFormula As StringBuilder,
       totalMass As Double,
       targetMass As Double,
       massToleranceDa As Double,
       totalCharge As Double,
       intMultipleMtoZCharge As Integer,
       empiricalResultSymbols As Dictionary(Of String, Integer)) As Boolean

        If Not SortResults Then
            ' Don't convert to formatted empirical formula

            For Each item In empiricalResultSymbols
                AppendToEmpiricalFormula(sbEmpiricalFormula, item.Key, item.Value)
            Next

        Else
            ' Convert to a formatted empirical formula (elements order by C, H, then alphabetical)

            Dim matchCount As Integer

            ' First find C
            If empiricalResultSymbols.TryGetValue("C", matchCount) Then
                sbEmpiricalFormula.Append("C")
                If matchCount > 1 Then sbEmpiricalFormula.Append(matchCount)
            End If

            ' Next find H
            If empiricalResultSymbols.TryGetValue("H", matchCount) Then
                sbEmpiricalFormula.Append("H")
                If matchCount > 1 Then sbEmpiricalFormula.Append(matchCount)
            End If

            Dim query = From item In empiricalResultSymbols Where item.Key <> "C" And item.Key <> "H" Order By item.Key Select item

            For Each result In query
                sbEmpiricalFormula.Append(result.Key)
                If result.Value > 1 Then sbEmpiricalFormula.Append(result.Value)
            Next

        End If

        If Not VerifyHydrogens And Not massSearchOptions.FindTargetMZ Then
            Return True
        End If

        ' Verify that the formula does not have too many hydrogens

        ' Counters for elements of interest (hydrogen, carbon, silicon, nitrogen, phosphorus, chlorine, iodine, flourine, bromine, and other)
        Dim udtElementNum As udtElementNumType

        ' Determine number of C, Si, N, P, O, S, Cl, I, F, Br and H atoms
        For Each item In empiricalResultSymbols
            Select Case item.Key
                Case "C" : udtElementNum.C = udtElementNum.C + item.Value
                Case "Si" : udtElementNum.Si = udtElementNum.Si + item.Value
                Case "N" : udtElementNum.N = udtElementNum.N + item.Value
                Case "P" : udtElementNum.P = udtElementNum.P + item.Value
                Case "O" : udtElementNum.O = udtElementNum.O + item.Value
                Case "S" : udtElementNum.S = udtElementNum.S + item.Value
                Case "Cl" : udtElementNum.Cl = udtElementNum.Cl + item.Value
                Case "I" : udtElementNum.I = udtElementNum.I + item.Value
                Case "F" : udtElementNum.F = udtElementNum.F + item.Value
                Case "Br" : udtElementNum.Br = udtElementNum.Br + item.Value
                Case "H" : udtElementNum.H = udtElementNum.H + item.Value
                Case Else : udtElementNum.Other = udtElementNum.Other + item.Value
            End Select
        Next

        Dim maxH As Integer = 0

        ' Compute maximum number of hydrogens
        If udtElementNum.Si = 0 AndAlso udtElementNum.C = 0 AndAlso udtElementNum.N = 0 AndAlso
           udtElementNum.P = 0 AndAlso udtElementNum.Other = 0 AndAlso
           (udtElementNum.O > 0 OrElse udtElementNum.S > 0) Then
            ' Only O and S
            maxH = 3
        Else
            ' Formula is: [#C*2 + 3 - (2 if N or P present)] + [#N + 3 - (1 if C or Si present)] + [#other elements * 4 + 3], where we assume other elements can have a coordination Number of up to 7
            If udtElementNum.C > 0 Or udtElementNum.Si > 0 Then
                maxH += (udtElementNum.C + udtElementNum.Si) * 2 + 3
                ' If udtElementNum.N > 0 Or udtElementNum.P > 0 Then maxh = maxh - 2
            End If

            If udtElementNum.N > 0 Or udtElementNum.P > 0 Then
                maxH += (udtElementNum.N + udtElementNum.P) + 3
                ' If udtElementNum.C > 0 Or udtElementNum.Si > 0 Then maxh = maxh - 1
            End If

            ' Correction for carbon contribution
            'If (udtElementNum.C > 0 Or udtElementNum.Si > 0) And (udtElementNum.N > 0 Or udtElementNum.P > 0) Then udtElementNum.H = udtElementNum.H - 2

            ' Correction for nitrogen contribution
            'If (udtElementNum.N > 0 Or udtElementNum.P > 0) And (udtElementNum.C > 0 Or udtElementNum.Si > 0) Then udtElementNum.H = udtElementNum.H - 1

            ' Combine the above two commented out if's to obtain:
            If (udtElementNum.N > 0 Or udtElementNum.P > 0) And (udtElementNum.C > 0 Or udtElementNum.Si > 0) Then
                maxH = maxH - 3
            End If

            If udtElementNum.Other > 0 Then maxH += udtElementNum.Other * 4 + 3

        End If

        ' correct for if H only
        If maxH < 3 Then maxH = 3

        ' correct for halogens
        maxH = maxH - udtElementNum.F - udtElementNum.Cl - udtElementNum.Br - udtElementNum.I

        ' correct for negative udtElementNum.H
        If maxH < 0 Then maxH = 0

        ' Verify H's
        Dim blnHOK = (udtElementNum.H <= maxH)

        ' Only proceed if hydrogens check out
        If Not blnHOK Then
            Return False
        End If

        Dim chargeOK As Boolean
        Dim correctedCharge = totalCharge

        ' See if totalCharge is within charge limits (chargeOK will be set to True or False by CorrectChargeEmpirical)
        If massSearchOptions.FindCharge Then
            correctedCharge = CorrectChargeEmpirical(massSearchOptions, totalCharge, udtElementNum, chargeOK)
        Else
            chargeOK = True
        End If

        ' If charge is within range and checking for multiples, see if correct m/z too
        If chargeOK AndAlso massSearchOptions.FindTargetMZ Then
            chargeOK = CheckMtoZWithTarget(totalMass, correctedCharge, targetMass,
                                              massToleranceDa, intMultipleMtoZCharge)
        End If

        Return chargeOK

    End Function

    ''' <summary>
    ''' Initializes a new search result
    ''' </summary>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="ppmMode"></param>
    ''' <param name="sbEmpiricalFormula"></param>
    ''' <param name="totalMass">If 0 or negative, means matching percent compositions, so don't want to add dm= to line</param>
    ''' <param name="targetMass"></param>
    ''' <param name="totalCharge"></param>
    ''' <remarks></remarks>
    Private Function GetSearchResult(
       massSearchOptions As clsFormulaFinderOptions,
       ppmMode As Boolean,
       sbEmpiricalFormula As StringBuilder,
       totalMass As Double,
       targetMass As Double,
       totalCharge As Double) As udtFormulaFinderMassResult

        Dim udtSearchResult = New udtFormulaFinderMassResult
        udtSearchResult.EmpiricalFormula = String.Empty
        udtSearchResult.PercentComposition = New Dictionary(Of String, Double)

        Try

            udtSearchResult.EmpiricalFormula = sbEmpiricalFormula.ToString()

            If massSearchOptions.FindCharge Then
                udtSearchResult.ChargeState = CInt(Math.Round(totalCharge))
            End If

            If targetMass > 0 Then

                If ppmMode Then
                    udtSearchResult.Mass = totalMass
                    udtSearchResult.DeltaMass = CDbl(((totalMass) / targetMass - 1) * 1000000.0#)
                    udtSearchResult.DeltaMassIsPPM = True
                Else
                    udtSearchResult.Mass = totalMass
                    udtSearchResult.DeltaMass = totalMass - targetMass
                    udtSearchResult.DeltaMassIsPPM = False
                End If

                If massSearchOptions.ComputeMZ AndAlso Math.Abs(totalCharge) > 0.1 Then
                    ' Compute m/z value
                    udtSearchResult.MZ = Math.Abs(totalMass / totalCharge)
                End If

            End If

            Return udtSearchResult

        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("GetSearchResult", 0, ex.Message)
            Return udtSearchResult
        End Try

    End Function

    ''' <summary>
    ''' Correct charge using rules for an empirical formula
    ''' </summary>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="totalCharge"></param>
    ''' <param name="udtElementNum"></param>
    ''' <param name="chargeOK"></param>
    ''' <returns>Corrected charge</returns>
    ''' <remarks></remarks>
    Private Function CorrectChargeEmpirical(
       massSearchOptions As clsFormulaFinderOptions,
       totalCharge As Double,
       udtElementNum As udtElementNumType,
       <Out> ByRef chargeOK As Boolean) As Double

        Dim correctedCharge = totalCharge

        If udtElementNum.C + udtElementNum.Si >= 1 Then
            If udtElementNum.H > 0 And Math.Abs(mElementAndMassRoutines.GetElementStatInternal(1, esElementStatsConstants.esCharge) - 1) < Single.Epsilon Then
                ' Since carbon or silicon are present, assume the hydrogens should be negative
                ' Subtract udtElementNum.h*2 since hydrogen is assigned a +1 charge if ElementStats(1).Charge = 1
                correctedCharge -= udtElementNum.H * 2
            End If

            ' Correct for udtElementNum.ber of C and Si
            If udtElementNum.C + udtElementNum.Si > 1 Then
                correctedCharge -= (udtElementNum.C + udtElementNum.Si - 1) * 2
            End If
        End If

        If udtElementNum.N + udtElementNum.P >= 1 And udtElementNum.C > 0 Then
            ' Assume 2 hydrogens around each Nitrogen or Phosphorus, thus add back +2 for each H
            ' First, decrease udtElementNum.ber of halogens by udtElementNum.ber of hydrogens & halogens taken up by the carbons
            ' Determine # of H taken up by all the carbons in a compound without N or P, then add back 1 H for each N and P
            Dim intNumHalogens = udtElementNum.H + udtElementNum.F + udtElementNum.Cl + udtElementNum.Br + udtElementNum.I
            intNumHalogens -= (udtElementNum.C * 2 + 2) + udtElementNum.N + udtElementNum.P

            If intNumHalogens >= 0 Then
                For intIndex = 1 To udtElementNum.N + udtElementNum.P
                    correctedCharge += 2
                    intNumHalogens -= 1

                    If intNumHalogens > 0 Then
                        correctedCharge += 2
                        intNumHalogens -= 1
                    End If

                    If intNumHalogens <= 0 Then Exit For

                Next intIndex
            End If
        End If

        If massSearchOptions.LimitChargeRange Then
            ' Make sure correctedCharge is within the specified range
            If correctedCharge >= massSearchOptions.ChargeMin AndAlso
               correctedCharge <= massSearchOptions.ChargeMax Then
                ' Charge is within range
                chargeOK = True
            Else
                chargeOK = False
            End If
        Else
            chargeOK = True
        End If

        Return correctedCharge

    End Function

    Private Sub EstimateNumberOfOperations(potentialElementCount As Integer, Optional multipleSearchMax As Integer = 0)

        ' Estimate the number of operations that will be performed
        mRecursiveCount = 0
        mRecursiveFunctionCallCount = 0

        If potentialElementCount = 1 Then
            mMaxRecursiveCount = 1
            Exit Sub
        End If

        Const NUM_POINTERS As Integer = 3

        ' Calculate lngMaxRecursiveCount based on a combination function
        Dim maxRecursiveCount = Combinatorial(NUM_POINTERS + potentialElementCount, potentialElementCount - 1) - Combinatorial(potentialElementCount + NUM_POINTERS - 2, NUM_POINTERS - 1)
        If maxRecursiveCount > Integer.MaxValue Then
            mMaxRecursiveCount = Integer.MaxValue
        Else
            mMaxRecursiveCount = CInt(maxRecursiveCount)
        End If

        If multipleSearchMax > 0 Then
            ' Correct lngMaxRecursiveCount for searching for m/z values
            mMaxRecursiveCount = mMaxRecursiveCount * multipleSearchMax
        End If

    End Sub

    ''' <summary>
    ''' Compute the factorial of a number; uses recursion
    ''' </summary>
    ''' <param name="value">Integer between 0 and 170</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function Factorial(value As Integer) As Double

        If value > 170 Then
            Throw New Exception("Cannot compute factorial of a number over 170")
        End If

        If value < 0 Then
            Throw New Exception("Cannot compute factorial of a negative number")
        End If

        If value = 0 Then
            Return 1
        Else
            Return value * Factorial(value - 1)
        End If

    End Function

    Private Function FindMatchesByMass(
       targetMass As Double,
       massToleranceDa As Double,
       massSearchOptions As clsFormulaFinderOptions,
       ppmMode As Boolean) As List(Of udtFormulaFinderMassResult)

        ' Validate the Inputs
        If Not ValidateSettings(eCalculationMode.MatchMolecularWeight) Then
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If Val(targetMass) <= 0 Then
            ReportError("Target molecular weight must be greater than 0")
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If massToleranceDa < 0 Then
            ReportError("Mass tolerance cannot be negative")
            Return New List(Of udtFormulaFinderMassResult)
        End If

        Const percentTolerance = 0                                              ' Not used here, but required for call to GetCandidateElements()
        Dim intRange(MAX_MATCHINGELEMENTS, 2) As Integer                        ' The min and max number for each element (first dimension is element index, second dimension is min and max value for that element)
        Dim dblPotentialElementStats(MAX_MATCHINGELEMENTS, 1) As Double         ' The elemental weight and charge for each element or abbreviation
        Dim strPotentialElements(MAX_MATCHINGELEMENTS) As String                ' The symbol for each element
        Dim dblTargetPercents(MAX_MATCHINGELEMENTS, 1) As Double                ' The min and max percentages for each element (not applicable in this function)

        Dim potentialElementCount = GetCandidateElements(percentTolerance, intRange, dblPotentialElementStats, strPotentialElements, dblTargetPercents)

        ' Dim candidateElementsStats = GetCandidateElements(percentTolerance)

        If potentialElementCount = 0 Then
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If SearchMode = eSearchMode.Thorough Then
            ' Thorough search

            EstimateNumberOfOperations(potentialElementCount)

            SortCandidateElements(eCalculationMode.MatchMolecularWeight, potentialElementCount, dblPotentialElementStats, strPotentialElements, dblTargetPercents)

            ' Pointers to the potential elements
            Dim lstPotentialElementPointers = New List(Of Integer)

            Dim lstResults = New List(Of udtFormulaFinderMassResult)

            If massSearchOptions.FindTargetMZ Then
                ' Searching for target m/z rather than target mass
                MultipleSearchMath(potentialElementCount, massSearchOptions)

                For x = massSearchOptions.ChargeMin To massSearchOptions.ChargeMax
                    ' Call the RecursiveMWfinder repeatedly, sending dblTargetWeight * x each time to search for target, target*2, target*3, etc.
                    RecursiveMWFinder(lstResults, massSearchOptions, ppmMode, strPotentialElements, dblPotentialElementStats, 0, potentialElementCount, lstPotentialElementPointers, 0, targetMass * x, massToleranceDa, 0, x)
                Next x

            Else
                RecursiveMWFinder(lstResults, massSearchOptions, ppmMode, strPotentialElements, dblPotentialElementStats, 0, potentialElementCount, lstPotentialElementPointers, 0, targetMass, massToleranceDa, 0, 0)
            End If

            ComputeSortKeys(lstResults)

            Return lstResults

        Else
            ' Bounded search
            Const maximumFormulaMass = 0
            
            Dim boundedSearchResults = BoundedSearch(targetMass, massToleranceDa, maximumFormulaMass,
                                                     massSearchOptions, ppmMode, eCalculationMode.MatchMolecularWeight,
                                                     potentialElementCount, dblPotentialElementStats, strPotentialElements,
                                                     intRange, dblTargetPercents)

            ComputeSortKeys(boundedSearchResults)

            Return boundedSearchResults
        End If

    End Function

    Private Function FindMatchesByPercentCompositionWork(
       maximumFormulaMass As Double,
       percentTolerance As Double,
       massSearchOptions As clsFormulaFinderOptions) As List(Of udtFormulaFinderMassResult)

        ' Validate the Inputs
        If Not ValidateSettings(eCalculationMode.MatchPercentComposition) Then
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If Val(maximumFormulaMass) <= 0 Then
            ReportError("Maximum molecular weight must be greater than 0")
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If percentTolerance < 0 Then
            ReportError("Percent tolerance cannot be negative")
            Return New List(Of udtFormulaFinderMassResult)
        End If

        Dim intRange(MAX_MATCHINGELEMENTS, 2) As Integer                        ' The min and max number for each element (first dimension is element index, second dimension is min and max value for that element)
        Dim dblPotentialElementStats(MAX_MATCHINGELEMENTS, 1) As Double         ' The elemental weight and charge for each element or abbreviation
        Dim strPotentialElements(MAX_MATCHINGELEMENTS) As String                ' The symbol for each element
        Dim dblTargetPercents(MAX_MATCHINGELEMENTS, 1) As Double                ' The min and max percentages for each element

        Dim potentialElementCount = GetCandidateElements(percentTolerance, intRange, dblPotentialElementStats, strPotentialElements, dblTargetPercents)
        If potentialElementCount = 0 Then
            Return New List(Of udtFormulaFinderMassResult)
        End If

        If SearchMode = eSearchMode.Thorough Then
            ' Thorough search

            EstimateNumberOfOperations(potentialElementCount)

            SortCandidateElements(eCalculationMode.MatchPercentComposition, potentialElementCount, dblPotentialElementStats, strPotentialElements, dblTargetPercents)

            ' Pointers to the potential elements
            Dim lstPotentialElementPointers = New List(Of Integer)

            Dim lstResults = New List(Of udtFormulaFinderMassResult)

            RecursivePCompFinder(lstResults, massSearchOptions, strPotentialElements, dblPotentialElementStats, 0, potentialElementCount, lstPotentialElementPointers, 0, dblTargetPercents, maximumFormulaMass, 9)

            ComputeSortKeys(lstResults)

            Return lstResults

        Else
            ' Bounded search

            Const targetMass = 0
            Const massToleranceDa = 0
            Const ppmMode As Boolean = False

            Dim boundedSearchResults = BoundedSearch(targetMass, massToleranceDa, maximumFormulaMass,
                                                     massSearchOptions, ppmMode, eCalculationMode.MatchPercentComposition,
                                                     potentialElementCount, dblPotentialElementStats, strPotentialElements,
                                                     intRange, dblTargetPercents)

            ComputeSortKeys(boundedSearchResults)

            Return boundedSearchResults
        End If

    End Function

    Private Function GetCandidateElements(ByVal percentTolerance As Double) As List(Of clsFormulaFinderCandidateElement)

        Dim candidateElementsStats = New List(Of clsFormulaFinderCandidateElement)

        Dim customElementCounter = 0
        Dim dblMass As Double
        Dim sngCharge As Single

        For Each item In mCandidateElements

            Dim candidateElement = New clsFormulaFinderCandidateElement(item.Key)

            candidateElement.CountMinimum = item.Value.MinimumCount
            candidateElement.CountMaximum = item.Value.MaximumCount

            If mElementAndMassRoutines.IsValidElementSymbol(item.Key) Then
                Dim elementID = mElementAndMassRoutines.GetElementIDInternal(item.Key)

                mElementAndMassRoutines.GetElementInternal(elementID, item.Key, dblMass, 0, sngCharge, 0)

                candidateElement.Mass = dblMass
                candidateElement.Charge = sngCharge

            Else
                ' Working with an abbreviation or simply a mass

                Dim customMass As Double

                If Double.TryParse(item.Key, customMass) Then
                    ' Custom element, only weight given so charge is 0
                    candidateElement.Mass = customMass
                    candidateElement.Charge = 0

                    customElementCounter += 1

                    ' Custom elements are named C1_ or C2_ or C3_ etc.
                    candidateElement.Symbol = "C" & customElementCounter & "_"
                Else
                    ' A single element or abbreviation was entered

                    ' Convert input to default format of first letter capitalized and rest lowercase
                    Dim abbrevSymbol = item.Key.Substring(0).ToUpper() & item.Key.Substring(1).ToLower()

                    For Each currentChar In abbrevSymbol
                        If Not (Char.IsLetter(currentChar) OrElse currentChar = "+" OrElse currentChar = "_") Then
                            ReportError("Custom elemental weights must contain only numbers or only letters; if letters are used, they must be for a single valid elemental symbol or abbreviation")
                            Return New List(Of clsFormulaFinderCandidateElement)
                        End If
                    Next

                    If String.IsNullOrWhiteSpace(abbrevSymbol) Then
                        ' Too short
                        ReportError("Custom elemental weight is empty; if letters are used, they must be for a single valid elemental symbol or abbreviation")
                        Return New List(Of clsFormulaFinderCandidateElement)
                    End If

                    ' See if this is an abbreviation
                    Dim intSymbolReference = mElementAndMassRoutines.GetAbbreviationIDInternal(abbrevSymbol)
                    If intSymbolReference < 1 Then

                        ReportError("Unknown element or abbreviation for custom elemental weight: " & abbrevSymbol)
                        Return New List(Of clsFormulaFinderCandidateElement)
                    End If

                    ' Found a normal abbreviation
                    Dim matchedAbbrevSymbol As String = String.Empty
                    Dim abbrevFormula As String = String.Empty
                    Dim blnIsAminoAcid As Boolean
                    mElementAndMassRoutines.GetAbbreviationInternal(intSymbolReference, matchedAbbrevSymbol, abbrevFormula, sngCharge, blnIsAminoAcid)

                    dblMass = mElementAndMassRoutines.ComputeFormulaWeight(abbrevFormula)

                    candidateElement.Mass = dblMass

                    candidateElement.Charge = sngCharge

                End If

            End If

            candidateElement.PercentCompMinimum = item.Value.TargetPercentComposition - percentTolerance  ' Lower bound of target percentage
            candidateElement.PercentCompMaximum = item.Value.TargetPercentComposition + percentTolerance  ' Upper bound of target percentage

            candidateElementsStats.Add(candidateElement)
        Next

        Return candidateElementsStats

    End Function

    Private Function GetCandidateElements(
       ByVal percentTolerance As Double,
       ByVal intRange(,) As Integer,
       ByVal dblPotentialElementStats(,) As Double,
       ByVal strPotentialElements() As String,
       ByVal dblTargetPercents(,) As Double) As Integer

        Dim potentialElementCount = 0
        Dim customElementCounter = 0
        Dim dblMass As Double
        Dim sngCharge As Single

        For Each item In mCandidateElements

            intRange(potentialElementCount, 0) = item.Value.MinimumCount
            intRange(potentialElementCount, 1) = item.Value.MaximumCount

            If mElementAndMassRoutines.IsValidElementSymbol(item.Key) Then
                Dim elementID = mElementAndMassRoutines.GetElementIDInternal(item.Key)

                mElementAndMassRoutines.GetElementInternal(elementID, item.Key, dblMass, 0, sngCharge, 0)

                dblPotentialElementStats(potentialElementCount, 0) = dblMass
                dblPotentialElementStats(potentialElementCount, 1) = sngCharge

                strPotentialElements(potentialElementCount) = item.Key
            Else
                ' Working with an abbreviation or simply a mass

                Dim customMass As Double

                If Double.TryParse(item.Key, customMass) Then
                    ' Custom element, only weight given so charge is 0
                    dblPotentialElementStats(potentialElementCount, 0) = customMass
                    dblPotentialElementStats(potentialElementCount, 1) = 0

                    customElementCounter += 1

                    ' Custom elements are named C1_ or C2_ or C3_ etc.
                    strPotentialElements(potentialElementCount) = "C" & customElementCounter & "_"
                Else
                    ' A single element or abbreviation was entered

                    ' Convert input to default format of first letter capitalized and rest lowercase
                    Dim abbrevSymbol = item.Key.Substring(0).ToUpper() & item.Key.Substring(1).ToLower()

                    For Each currentChar In abbrevSymbol
                        If Not (Char.IsLetter(currentChar) OrElse currentChar = "+" OrElse currentChar = "_") Then
                            ReportError("Custom elemental weights must contain only numbers or only letters; if letters are used, they must be for a single valid elemental symbol or abbreviation")
                            Return 0
                        End If
                    Next

                    If String.IsNullOrWhiteSpace(abbrevSymbol) Then
                        ' Too short
                        ReportError("Custom elemental weight is empty; if letters are used, they must be for a single valid elemental symbol or abbreviation")
                        Return 0
                    End If

                    Dim charge = 0

                    ' See if this is an abbreviation
                    Dim intSymbolReference = mElementAndMassRoutines.GetAbbreviationIDInternal(abbrevSymbol)
                    If intSymbolReference < 1 Then

                        ReportError("Unknown element or abbreviation for custom elemental weight: " & abbrevSymbol)
                        Return 0
                    End If

                    ' Found a normal abbreviation
                    Dim matchedAbbrevSymbol As String = String.Empty
                    Dim abbrevFormula As String = String.Empty
                    Dim blnIsAminoAcid As Boolean
                    mElementAndMassRoutines.GetAbbreviationInternal(intSymbolReference, matchedAbbrevSymbol, abbrevFormula, sngCharge, blnIsAminoAcid)

                    dblMass = mElementAndMassRoutines.ComputeFormulaWeight(abbrevFormula)

                    ' Returns weight of element/abbreviation, but also charge
                    dblPotentialElementStats(potentialElementCount, 0) = dblMass

                    dblPotentialElementStats(potentialElementCount, 1) = charge

                    ' No problems, store symbol
                    strPotentialElements(potentialElementCount) = matchedAbbrevSymbol

                End If

            End If

            dblTargetPercents(potentialElementCount, 0) = item.Value.TargetPercentComposition - percentTolerance  ' Lower bound of target percentage
            dblTargetPercents(potentialElementCount, 1) = item.Value.TargetPercentComposition + percentTolerance  ' Upper bound of target percentage

            potentialElementCount += 1
        Next

        Return potentialElementCount

    End Function

    Private Function GetDefaultCandidateElementTolerance(Optional targetPercentComposition As Double = 0) As udtCandidateElementTolerances

        Dim udtElementTolerances = New udtCandidateElementTolerances

        udtElementTolerances.MinimumCount = 0               ' Only used with the Bounded search mode
        udtElementTolerances.MaximumCount = 10              ' Only used with the Bounded search mode

        udtElementTolerances.TargetPercentComposition = targetPercentComposition   ' Only used when searching for percent compositions

        Return udtElementTolerances

    End Function

    Private Function GetTotalPercentComposition() As Double
        Dim totalTargetPercentComp = mCandidateElements.Sum(Function(item) item.Value.TargetPercentComposition)
        Return totalTargetPercentComp

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="massSearchOptions"></param>
    ''' <remarks>massSearchOptions is passed ByRef because it is a value type and .MzChargeMin and .MzChargeMax are updated</remarks>
    Private Sub MultipleSearchMath(potentialElementCount As Integer, massSearchOptions As clsFormulaFinderOptions)

        Dim multipleSearchMin = massSearchOptions.ChargeMin
        Dim multipleSearchMax = massSearchOptions.ChargeMax

        Dim ultipleSearchMax = Math.Max(Math.Abs(multipleSearchMin), Math.Abs(multipleSearchMax))
        multipleSearchMin = 1

        If ultipleSearchMax < multipleSearchMin Then multipleSearchMax = multipleSearchMin

        massSearchOptions.ChargeMin = multipleSearchMin
        massSearchOptions.ChargeMax = multipleSearchMax

        EstimateNumberOfOperations(potentialElementCount, multipleSearchMax)

    End Sub

    ''' <summary>
    ''' Formula finder that uses a series of nested for loops and is thus slow when a large number of candidate elements 
    ''' or when elements have a large range of potential counts
    ''' </summary>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="ppmMode"></param>
    ''' <param name="calculationMode"></param>
    ''' <param name="strPotentialElements"></param>
    ''' <param name="dblPotentialElementStats"></param>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="targetMass">Only used when calculationMode is MatchMolecularWeight</param>
    ''' <param name="massToleranceDa">Only used when calculationMode is MatchMolecularWeigh</param>
    ''' <param name="maximumFormulaMass">Only used when calculationMode is MatchPercentComposition</param>
    ''' <param name="intRange">The min and max number for each element (first dimension is element index, second dimension is min and max value for that element)</param>
    ''' <param name="dblTargetPercents"></param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function OldFormulaFinder(
       massSearchOptions As clsFormulaFinderOptions,
       ppmMode As Boolean,
       calculationMode As eCalculationMode,
       strPotentialElements() As String,
       dblPotentialElementStats(,) As Double,
       potentialElementCount As Integer,
       targetMass As Double,
       massToleranceDa As Double,
       maximumFormulaMass As Double,
       intRange(,) As Integer,
       dblTargetPercents(,) As Double) As List(Of udtFormulaFinderMassResult)

        ' The calculated percentages for the specific compound
        Dim Percent(MAX_MATCHINGELEMENTS) As Double

        Dim lstResults = New List(Of udtFormulaFinderMassResult)

        Try

            ' Only used when calculationMode is MatchMolecularWeight
            Dim dblMultipleSearchMaxWeight = targetMass * massSearchOptions.ChargeMax

            Dim sbEmpiricalFormula = New StringBuilder()

            ' Determine the valid compounds
            For j = intRange(0, 0) To intRange(0, 1)
                For k = intRange(1, 0) To intRange(1, 1)
                    For l = intRange(2, 0) To intRange(2, 1)
                        For m = intRange(3, 0) To intRange(3, 1)
                            For N = intRange(4, 0) To intRange(4, 1)
                                For O = intRange(5, 0) To intRange(5, 1)
                                    For P = intRange(6, 0) To intRange(6, 1)
                                        For q = intRange(7, 0) To intRange(7, 1)
                                            For r = intRange(8, 0) To intRange(8, 1)
                                                For S = intRange(9, 0) To intRange(9, 1)


                                                    Dim totalMass = j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + N * dblPotentialElementStats(4, 0) + O * dblPotentialElementStats(5, 0) + P * dblPotentialElementStats(6, 0) + q * dblPotentialElementStats(7, 0) + r * dblPotentialElementStats(8, 0) + S * dblPotentialElementStats(9, 0)
                                                    Dim totalCharge = j * dblPotentialElementStats(0, 1) + k * dblPotentialElementStats(1, 1) + l * dblPotentialElementStats(2, 1) + m * dblPotentialElementStats(3, 1) + N * dblPotentialElementStats(4, 1) + O * dblPotentialElementStats(5, 1) + P * dblPotentialElementStats(6, 1) + q * dblPotentialElementStats(7, 1) + r * dblPotentialElementStats(8, 1) + S * dblPotentialElementStats(9, 1)

                                                    If calculationMode = eCalculationMode.MatchPercentComposition Then
                                                        ' Matching Percent Compositions
                                                        If totalMass > 0 And totalMass <= maximumFormulaMass Then
                                                            Percent(0) = j * dblPotentialElementStats(0, 0) / totalMass * 100
                                                            Percent(1) = k * dblPotentialElementStats(1, 0) / totalMass * 100
                                                            Percent(2) = l * dblPotentialElementStats(2, 0) / totalMass * 100
                                                            Percent(3) = m * dblPotentialElementStats(3, 0) / totalMass * 100
                                                            Percent(4) = N * dblPotentialElementStats(4, 0) / totalMass * 100
                                                            Percent(5) = O * dblPotentialElementStats(5, 0) / totalMass * 100
                                                            Percent(6) = P * dblPotentialElementStats(6, 0) / totalMass * 100
                                                            Percent(7) = q * dblPotentialElementStats(7, 0) / totalMass * 100
                                                            Percent(8) = r * dblPotentialElementStats(8, 0) / totalMass * 100
                                                            Percent(9) = S * dblPotentialElementStats(9, 0) / totalMass * 100

                                                            Dim intSubTrack = 0
                                                            For intIndex = 0 To potentialElementCount - 1
                                                                If Percent(intIndex) >= dblTargetPercents(intIndex, 0) AndAlso
                                                                   Percent(intIndex) <= dblTargetPercents(intIndex, 1) Then
                                                                    intSubTrack += 1
                                                                End If
                                                            Next intIndex

                                                            If intSubTrack = potentialElementCount Then
                                                                ' All of the elements have percent compositions matching the target

                                                                ' Construct the empirical formula and verify hydrogens
                                                                Dim blnHOK = ConstructAndVerifyCompound(massSearchOptions,
                                                                                                        sbEmpiricalFormula,
                                                                                                        j, k, l, m, N, O, P, q, r, S,
                                                                                                        strPotentialElements(0), strPotentialElements(1), strPotentialElements(2), strPotentialElements(3), strPotentialElements(4), strPotentialElements(5), strPotentialElements(6), strPotentialElements(7), strPotentialElements(8), strPotentialElements(9),
                                                                                                        totalMass, targetMass, massToleranceDa,
                                                                                                        totalCharge, 0)


                                                                If sbEmpiricalFormula.Length > 0 AndAlso blnHOK Then
                                                                    Dim udtSearchResult = GetSearchResult(massSearchOptions, ppmMode, sbEmpiricalFormula, totalMass, -1, totalCharge)

                                                                    ' Add % composition info

                                                                    AppendPercentCompositionResult(udtSearchResult, j, strPotentialElements(0), Percent(0))
                                                                    AppendPercentCompositionResult(udtSearchResult, k, strPotentialElements(1), Percent(1))
                                                                    AppendPercentCompositionResult(udtSearchResult, l, strPotentialElements(2), Percent(2))
                                                                    AppendPercentCompositionResult(udtSearchResult, m, strPotentialElements(3), Percent(3))
                                                                    AppendPercentCompositionResult(udtSearchResult, N, strPotentialElements(4), Percent(4))
                                                                    AppendPercentCompositionResult(udtSearchResult, O, strPotentialElements(5), Percent(5))
                                                                    AppendPercentCompositionResult(udtSearchResult, P, strPotentialElements(6), Percent(6))
                                                                    AppendPercentCompositionResult(udtSearchResult, q, strPotentialElements(7), Percent(7))
                                                                    AppendPercentCompositionResult(udtSearchResult, r, strPotentialElements(8), Percent(8))
                                                                    AppendPercentCompositionResult(udtSearchResult, S, strPotentialElements(9), Percent(9))

                                                                    lstResults.Add(udtSearchResult)
                                                                End If
                                                            End If
                                                        End If

                                                    Else
                                                        ' Matching Molecular Weights

                                                        If totalMass <= dblMultipleSearchMaxWeight + massToleranceDa Then

                                                            ' When massSearchOptions.FindTargetMZ is false, ChargeMin and ChargeMax will be 1
                                                            For intCurrentCharge = massSearchOptions.ChargeMin To massSearchOptions.ChargeMax

                                                                Dim dblMatchWeight = targetMass * intCurrentCharge
                                                                If totalMass <= dblMatchWeight + massToleranceDa AndAlso
                                                                   totalMass >= dblMatchWeight - massToleranceDa Then
                                                                    ' Within massToleranceDa

                                                                    ' Construct the empirical formula and verify hydrogens
                                                                    Dim blnHOK = ConstructAndVerifyCompound(massSearchOptions,
                                                                                                            sbEmpiricalFormula,
                                                                                                            j, k, l, m, N, O, P, q, r, S,
                                                                                                            strPotentialElements(0), strPotentialElements(1), strPotentialElements(2), strPotentialElements(3), strPotentialElements(4), strPotentialElements(5), strPotentialElements(6), strPotentialElements(7), strPotentialElements(8), strPotentialElements(9),
                                                                                                            totalMass, targetMass * intCurrentCharge, massToleranceDa,
                                                                                                            totalCharge, intCurrentCharge)

                                                                    If sbEmpiricalFormula.Length > 0 AndAlso blnHOK Then
                                                                        Dim udtSearchResult = GetSearchResult(massSearchOptions, ppmMode, sbEmpiricalFormula, totalMass, targetMass, totalCharge)

                                                                        lstResults.Add(udtSearchResult)
                                                                    End If
                                                                    Exit For
                                                                End If
                                                            Next intCurrentCharge
                                                        Else

                                                            ' Jump out of loop since weight is too high
                                                            ' Determine which variable is causing the weight to be too high
                                                            ' Incrementing "s" would definitely make the weight too high, so set it to its max (so it will zero and increment "r")
                                                            S = intRange(9, 1)
                                                            If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + N * dblPotentialElementStats(4, 0) + O * dblPotentialElementStats(5, 0) + P * dblPotentialElementStats(6, 0) + q * dblPotentialElementStats(7, 0) + (r + 1) * dblPotentialElementStats(8, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                ' Incrementing r would make the weight too high, so set it to its max (so it will zero and increment q)
                                                                r = intRange(8, 1)
                                                                If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + N * dblPotentialElementStats(4, 0) + O * dblPotentialElementStats(5, 0) + P * dblPotentialElementStats(6, 0) + (q + 1) * dblPotentialElementStats(7, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                    q = intRange(7, 1)
                                                                    If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + N * dblPotentialElementStats(4, 0) + O * dblPotentialElementStats(5, 0) + (P + 1) * dblPotentialElementStats(6, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                        P = intRange(6, 1)
                                                                        If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + N * dblPotentialElementStats(4, 0) + (O + 1) * dblPotentialElementStats(5, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                            O = intRange(5, 1)
                                                                            If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + m * dblPotentialElementStats(3, 0) + (N + 1) * dblPotentialElementStats(4, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                                N = intRange(4, 1)
                                                                                If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + l * dblPotentialElementStats(2, 0) + (m + 1) * dblPotentialElementStats(3, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                                    m = intRange(3, 1)
                                                                                    If (j * dblPotentialElementStats(0, 0) + k * dblPotentialElementStats(1, 0) + (l + 1) * dblPotentialElementStats(2, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                                        l = intRange(2, 1)
                                                                                        If (j * dblPotentialElementStats(0, 0) + (k + 1) * dblPotentialElementStats(1, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                                            k = intRange(1, 1)
                                                                                            If ((j + 1) * dblPotentialElementStats(0, 0)) > (massToleranceDa + dblMultipleSearchMaxWeight) Then
                                                                                                j = intRange(0, 1)
                                                                                            End If
                                                                                        End If
                                                                                    End If
                                                                                End If
                                                                            End If
                                                                        End If
                                                                    End If
                                                                End If
                                                            End If
                                                        End If
                                                    End If


                                                    If mAbortProcessing Then
                                                        Return lstResults
                                                    End If

                                                    If lstResults.Count >= mMaximumHits Then

                                                        ' Set variables to their maximum so all the loops will end
                                                        j = intRange(0, 1)
                                                        k = intRange(1, 1)
                                                        l = intRange(2, 1)
                                                        m = intRange(3, 1)
                                                        N = intRange(4, 1)
                                                        O = intRange(5, 1)
                                                        P = intRange(6, 1)
                                                        q = intRange(7, 1)
                                                        r = intRange(8, 1)
                                                        S = intRange(9, 1)
                                                    End If

                                                Next S
                                            Next r
                                        Next q
                                    Next P
                                Next O
                            Next N
                        Next m
                    Next l
                Next k

                If intRange(0, 1) <> 0 Then
                    ' ToDo: Validate this logic.  Should it be if .ChargeMin = .ChargeMax?
                    If massSearchOptions.ChargeMin = 0 Then
                        mPercentComplete = j / intRange(0, 1) * 100

                    Else
                        ' ToDo: Validate this logic.  Should it be (.Chargemax - .ChargeMin) ?
                        mPercentComplete = j / (intRange(0, 1) * massSearchOptions.ChargeMax) * 100

                    End If
                End If

            Next j

        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("OldFormulaFinder", 0, ex.Message)
        End Try

        Return lstResults

    End Function

    Private Sub SortCandidateElements(
       calculationMode As eCalculationMode,
       potentialElementCount As Integer,
       dblPotentialElementStats(,) As Double,
       strPotentialElements() As String,
       dblTargetPercents(,) As Double)

        ' Reorder dblPotentialElementStats pointer array in order from heaviest to lightest element
        ' Greatly speeds up the recursive routine

        ' Bubble sort
        For y = potentialElementCount - 1 To 1 Step -1       ' Sort from end to start
            For x = 0 To y - 1
                If dblPotentialElementStats(x, 0) < dblPotentialElementStats(x + 1, 0) Then
                    ' Swap the element symbols
                    Dim strSwap = strPotentialElements(x)
                    strPotentialElements(x) = strPotentialElements(x + 1)
                    strPotentialElements(x + 1) = strSwap

                    ' and their weights
                    Dim dblSwapVal = dblPotentialElementStats(x, 0)
                    dblPotentialElementStats(x, 0) = dblPotentialElementStats(x + 1, 0)
                    dblPotentialElementStats(x + 1, 0) = dblSwapVal

                    ' and their charge
                    dblSwapVal = dblPotentialElementStats(x, 1)
                    dblPotentialElementStats(x, 1) = dblPotentialElementStats(x + 1, 1)
                    dblPotentialElementStats(x + 1, 1) = dblSwapVal

                    If calculationMode = eCalculationMode.MatchPercentComposition Then
                        ' and the dblTargetPercents array
                        dblSwapVal = dblTargetPercents(x, 0)
                        dblTargetPercents(x, 0) = dblTargetPercents(x + 1, 0)
                        dblTargetPercents(x + 1, 0) = dblSwapVal

                        dblSwapVal = dblTargetPercents(x, 1)
                        dblTargetPercents(x, 1) = dblTargetPercents(x + 1, 1)
                        dblTargetPercents(x + 1, 1) = dblSwapVal

                    End If
                End If
            Next x
        Next y

    End Sub

    'ToBeDeleted: Private Function FormulaFinderCalculate(calculationMode As eCalculationMode) As List(Of udtFormulaFinderMassResult)

    '    Dim potentialElementCount As Integer, blnCalculationsAborted As Boolean
    '    Dim x As Integer, y As Integer
    '    Dim blnBad As Boolean
    '    Dim intMultipleSearchMin As Integer, intMultipleSearchMax As Integer
    '    Dim tolerance As Double, dblTargetWeight As Double, percentcompsum As Double
    '    Dim strMessage As String, strWork As String, strSwap As String
    '    Dim dblSwapVal As Double
    '    Dim strMatchSymbol As String, Charge As Single


    '    Dim intPotentialElementPointers(0) As Integer                           ' Empty array of pointers to the potential elements
    '    Dim intRange(MAX_MATCHINGELEMENTS, 2) As Integer                        ' The min and max number for each element (first dimension is element index, second dimension is min and max value for that element)
    '    Dim dblPotentialElementStats(MAX_MATCHINGELEMENTS, 1) As Double         ' The elemental weight and charge for each element or abbreviation
    '    Dim strPotentialElements(MAX_MATCHINGELEMENTS) As String                ' The symbol for each element or abbreviation
    '    Dim dblTargetPercents(MAX_MATCHINGELEMENTS, 1) As Double                ' The min and max percentages for each element (if applicable)

    '    Dim strCompoundList() As String

    '    If mCalculating Then Exit Function

    '    mAbortProcessing = False
    '    blnCalculationsAborted = False

    '    Dim lstResults = New List(Of udtFormulaFinderMassResult)

    '    Try

    '        mCalculating = True
    '        mPercentComplete = 0

    '        If frmFinderOptions.cboSearchType.ListIndex = 0 Then
    '            ' Thorough search

    '        Else
    '            ' Bounded search

    '        End If

    '        ' Show abort messages if necessary
    '        If mAbortProcessing Then
    '            ReportWarning("Calculations aborted")
    '            blnCalculationsAborted = True
    '        ElseIf lstResults.Count >= mMaximumHits Then
    '            ReportWarning("The maximum number of hits has been reached." & "  " & "Stopping calculations.")
    '        End If


    '        ' Compute the sort key for each result
    '        For Each item In lstResults
    '            item.SortKey = ComputeSortKey(item.EmpiricalFormula)
    '        Next

    '        ReDim strCodestring(lstResults.ListCount + 1)
    '        ReDim udtResultStats(lstResults.ListCount + 1)
    '        ReDim intPointerArray(lstResults.ListCount + 1)

    '        ' Convert compounds in list to code
    '        CompoundToCode(dblTargetWeight, strCodestring(), udtResultStats(), intPointerArray())

    '        ' Sort the compounds
    '        ShellSortResults(strCodestring(), udtResultStats(), intPointerArray(), 0, lstResults.ListCount - 1)

    '        ReDim strCompoundList(lstResults.ListCount)            ' Temporary storage for the results box

    '        ' First, copy all of the results to a temporary array (I know it eats up memory, but I have no choice)
    '        For x = 0 To lstResults.ListCount - 1
    '            strCompoundList(x) = lstResults.List(x)
    '        Next x

    '        ' Now, put them back into the lstResults.ListCount box in the correct order
    '        ' Use intPointerArray() for this
    '        For x = 0 To lstResults.ListCount - 1
    '            lstResults.List(x) = strCompoundList(intPointerArray(x))
    '        Next x

    '        If gKeyPressAbortFormulaFinder < 2 Then
    '            lblPercentCompleted.Caption = "100% " & "Completed"
    '        Else
    '            lblPercentCompleted.Caption = "Sorting Interrupted"
    '        End If
    '        Else
    '        ' Don't sort
    '        If gKeyPressAbortFormulaFinder < 2 Then
    '            lblPercentCompleted.Caption = "100% " & "Completed"
    '        Else
    '            lblPercentCompleted.Caption = "Calculations Interrupted"
    '        End If
    '        End If

    '        If optType(1).value = True Then
    '            ' Matching percent compositions
    '            ' Separate results into two lines per compound for better readability

    '            ' First, change view of lstresults box so first line is on top, avoids screen update problem when sorting
    '            If lstResults.ListCount > 0 Then
    '                lstResults.ListIndex = 0
    '                lstResults.ListIndex = -1
    '            End If
    '            x = 0
    '            Do While x <= lstResults.ListCount - 1
    '                y = InStr(lstResults.List(x), " " & "has")
    '                If y > 0 Then
    '                    lstResults.AddItem(Mid(lstResults.List(x), y), x + 1)
    '                    lstResults.List(x) = Left(lstResults.List(x), y - 1)
    '                    x = x + 1
    '                End If
    '                x = x + 1
    '            Loop
    '        End If

    '        ' Add line for total number of compounds found
    '        strMessage = "Compounds found" & ": " & Str(lngCount)
    '        lstResults.AddItem(strMessage, 0)

    '        ' Set the ToolTipText for the listing
    '        If lngCount > 0 And cChkBox(frmProgramPreferences.chkShowToolTips) Then
    '            lstResults.ToolTipText = LookupToolTipLanguageCaption(10200, "Double click any line to expand it")
    '        Else
    '            lstResults.ToolTipText = ""
    '        End If

    '        mCalculating = False

    '        ' Copy results from lstResults to rtfResults
    '        If gKeyPressAbortFormulaFinder < 2 Then
    '            ConvertListToRTF()
    '            lstResults.Visible = False
    '            rtfResults.Visible = True
    '        End If

    '        If blnCalculationsAborted Then
    '            lblPercentCompleted.Caption = "Calculations Interrupted"
    '        End If


    '    Catch ex As Exception
    '        mElementAndMassRoutines.GeneralErrorHandler("FormulaFinderCalculate", 0, ex.Message)
    '    End Try

    '    mCalculating = False


    'End Function

    ''' <summary>
    ''' Recursively serch for a target mass
    ''' </summary>
    ''' <param name="lstResults"></param>
    ''' <param name="massSearchOptions"></param>
    ''' <param name="strPotentialElements"></param>
    ''' <param name="dblPotentialElementStats">Mass and Charge of each of the potential elements</param>
    ''' <param name="intStartIndex"></param>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="lstPotentialElementPointers">Pointers to the elements that have been added to the potential formula so far</param>
    ''' <param name="dblPotentialMassTotal">Weight of the potential formula</param>
    ''' <param name="targetMass"></param>
    ''' <param name="massToleranceDa"></param>
    ''' <param name="potentialChargeTotal"></param>
    ''' <param name="intMultipleMtoZCharge">When massSearchOptions.FindTargetMZ is false, this will be 0; otherwise, the current charge being searched for</param>
    ''' <remarks></remarks>
    Private Sub RecursiveMWFinder(
       lstResults As ICollection(Of udtFormulaFinderMassResult),
       massSearchOptions As clsFormulaFinderOptions,
       ppmMode As Boolean,
       strPotentialElements() As String,
       dblPotentialElementStats(,) As Double,
       intStartIndex As Integer,
       potentialElementCount As Integer,
       lstPotentialElementPointers As List(Of Integer),
       dblPotentialMassTotal As Double,
       targetMass As Double,
       massToleranceDa As Double,
       potentialChargeTotal As Double,
       intMultipleMtoZCharge As Integer)

        Try

            Dim lstNewPotentialElementPointers = New List(Of Integer)(lstPotentialElementPointers.Count + 1)

            If mAbortProcessing Or lstResults.Count >= mMaximumHits Then
                Exit Sub
            End If

            Dim sbEmpiricalFormula = New StringBuilder()

            For intCurrentIndex = intStartIndex To potentialElementCount - 1  ' potentialElementCount >= 1, if 1, means just dblPotentialElementStats(0, 0), etc.
                Dim totalMass = dblPotentialMassTotal + dblPotentialElementStats(intCurrentIndex, 0)
                Dim totalCharge = potentialChargeTotal + dblPotentialElementStats(intCurrentIndex, 1)

                If totalMass <= targetMass + massToleranceDa Then
                    ' Below or within dblMassTolerance, add current element's pointer to pointer array
                    lstNewPotentialElementPointers.AddRange(lstPotentialElementPointers)

                    ' Append the current element's number
                    lstNewPotentialElementPointers.Add(intCurrentIndex)
                    '            ReportCompound "", strPotentialElements(), dblPotentialElementStats(), potentialElementCount, intNewPotentialElementPointers(), intPointerCount + 1, totalMass, targetMass, dblMassTolerance, totalCharge, intMultipleMtoZCharge

                    ' Update status
                    UpdateStatus()

                    If mAbortProcessing OrElse lstResults.Count >= mMaximumHits Then
                        Exit Sub
                    End If

                    If totalMass >= targetMass - massToleranceDa Then

                        ' Construct the empirical formula and verify hydrogens
                        Dim blnHOK = ConstructAndVerifyCompoundRecursive(massSearchOptions, sbEmpiricalFormula, strPotentialElements, potentialElementCount, lstNewPotentialElementPointers, totalMass, targetMass, massToleranceDa, totalCharge, intMultipleMtoZCharge)

                        If sbEmpiricalFormula.Length > 0 AndAlso blnHOK Then
                            Dim udtSearchResult = GetSearchResult(massSearchOptions, ppmMode, sbEmpiricalFormula, totalMass, targetMass, totalCharge)

                            lstResults.Add(udtSearchResult)
                        End If

                    End If

                    ' Haven't reached targetMass - dblMassTolerance region, so call RecursiveFinder again
                    If intCurrentIndex = potentialElementCount - 1 Then
                        ' But first, if adding the lightest element (i.e. the last in the list),
                        ' add a bunch of it until the potential compound's weight is close to the target

                        Dim intExtra = 0
                        Do While totalMass < targetMass - massToleranceDa - dblPotentialElementStats(intCurrentIndex, 0)
                            intExtra += 1
                            totalMass += dblPotentialElementStats(intCurrentIndex, 0)
                            totalCharge += dblPotentialElementStats(intCurrentIndex, 1)
                        Loop

                        If intExtra > 0 Then

                            For intPointer = 1 To intExtra
                                lstNewPotentialElementPointers.Add(intCurrentIndex)
                            Next intPointer

                        End If
                    End If

                    ' Now recursively call this sub
                    RecursiveMWFinder(lstResults, massSearchOptions, ppmMode, strPotentialElements, dblPotentialElementStats, intCurrentIndex, potentialElementCount, lstNewPotentialElementPointers, totalMass, targetMass, massToleranceDa, totalCharge, intMultipleMtoZCharge)
                End If
            Next intCurrentIndex

        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("RecursiveMWFinder", 0, ex.Message)
            mAbortProcessing = True
        End Try

    End Sub

    ''' <summary>
    ''' Recursively search for target percent composition values
    ''' </summary>
    ''' <param name="lstResults"></param>
    ''' <param name="strPotentialElements"></param>
    ''' <param name="dblPotentialElementStats">Mass and Charge of each of the potential elements</param>
    ''' <param name="intStartIndex"></param>
    ''' <param name="potentialElementCount"></param>
    ''' <param name="lstPotentialElementPointers">Pointers to the elements that have been added to the potential formula so far</param>
    ''' <param name="dblPotentialMassTotal">>Weight of the potential formula</param>
    ''' <param name="dblTargetPercents">The lower and upper bounds of target percentage for each potential element</param>
    ''' <param name="maximumFormulaMass"></param>
    ''' <param name="potentialChargeTotal"></param>
    ''' <remarks></remarks>
    Private Sub RecursivePCompFinder(
       lstResults As ICollection(Of udtFormulaFinderMassResult),
       massSearchOptions As clsFormulaFinderOptions,
       strPotentialElements() As String,
       dblPotentialElementStats(,) As Double,
       intStartIndex As Integer,
       potentialElementCount As Integer,
       lstPotentialElementPointers As List(Of Integer),
       dblPotentialMassTotal As Double,
       dblTargetPercents(,) As Double,
       maximumFormulaMass As Double,
       potentialChargeTotal As Double)

        Try

            Dim lstNewPotentialElementPointers = New List(Of Integer)(lstPotentialElementPointers.Count + 1)

            Dim dblPotentialPercents(potentialElementCount) As Double

            If mAbortProcessing Or lstResults.Count >= mMaximumHits Then
                Exit Sub
            End If

            Dim sbEmpiricalFormula = New StringBuilder()
            Const ppmMode = False

            For intCurrentIndex = intStartIndex To potentialElementCount - 1  ' potentialElementCount >= 1, if 1, means just dblPotentialElementStats(0,0), etc.
                Dim totalMass = dblPotentialMassTotal + dblPotentialElementStats(intCurrentIndex, 0)
                Dim totalCharge = potentialChargeTotal + dblPotentialElementStats(intCurrentIndex, 1)

                If totalMass <= maximumFormulaMass Then
                    ' only proceed if weight is less than max weight

                    lstNewPotentialElementPointers.AddRange(lstPotentialElementPointers)

                    ' Append the current element's number
                    lstNewPotentialElementPointers.Add(intCurrentIndex)

                    ' Compute the number of each element
                    Dim elementCountArray = GetElementCountArray(potentialElementCount, lstNewPotentialElementPointers)

                    Dim nonZeroCount = (From item In elementCountArray Where item > 0).Count

                    ' Only proceed if all elements are present
                    If nonZeroCount = potentialElementCount Then

                        ' Compute % comp of each element
                        For intIndex = 0 To potentialElementCount - 1
                            dblPotentialPercents(intIndex) = elementCountArray(intIndex) * dblPotentialElementStats(intIndex, 0) / totalMass * 100
                        Next intIndex
                        'If intPointerCount = 0 Then dblPotentialPercents(0) = 100

                        Dim intPercentTrack = 0
                        For intIndex = 0 To potentialElementCount - 1
                            If dblPotentialPercents(intIndex) >= dblTargetPercents(intIndex, 0) And _
                               dblPotentialPercents(intIndex) <= dblTargetPercents(intIndex, 1) Then
                                intPercentTrack += 1
                            End If
                        Next intIndex

                        If intPercentTrack = potentialElementCount Then
                            ' Matching compound

                            ' Construct the empirical formula and verify hydrogens
                            Dim blnHOK = ConstructAndVerifyCompoundRecursive(massSearchOptions, sbEmpiricalFormula, strPotentialElements, potentialElementCount, lstNewPotentialElementPointers, totalMass, 0, 0, totalCharge, 0)

                            If sbEmpiricalFormula.Length > 0 AndAlso blnHOK Then
                                Dim udtSearchResult = GetSearchResult(massSearchOptions, ppmMode, sbEmpiricalFormula, totalMass, -1, totalCharge)

                                ' Add % composition info
                                For intIndex = 0 To potentialElementCount - 1
                                    If elementCountArray(intIndex) <> 0 Then
                                        Dim percentComposition = elementCountArray(intIndex) * dblPotentialElementStats(intIndex, 0) / totalMass * 100

                                        AppendPercentCompositionResult(udtSearchResult, elementCountArray(intIndex), strPotentialElements(intIndex), percentComposition)

                                    End If
                                Next intIndex

                                lstResults.Add(udtSearchResult)
                            End If

                        End If

                    End If

                    ' Update status
                    UpdateStatus()

                    If mAbortProcessing OrElse lstResults.Count >= mMaximumHits Then
                        Exit Sub
                    End If

                    ' Haven't reached maximumFormulaMass
                    ' Now recursively call this sub
                    RecursivePCompFinder(lstResults, massSearchOptions, strPotentialElements, dblPotentialElementStats, intCurrentIndex, potentialElementCount, lstNewPotentialElementPointers, totalMass, dblTargetPercents, maximumFormulaMass, totalCharge)

                End If
            Next intCurrentIndex


        Catch ex As Exception
            mElementAndMassRoutines.GeneralErrorHandler("RecursivePCompFinder", 0, ex.Message)
            mAbortProcessing = True
        End Try

    End Sub

    Protected Sub ReportError(strErrorMessage As String)
        mErrorMessage = strErrorMessage
        If EchoMessagesToConsole Then Console.WriteLine(strErrorMessage)

        RaiseEvent ErrorEvent(strErrorMessage)
    End Sub

    Protected Sub ReportWarning(strWarningMessage As String)
        If EchoMessagesToConsole Then Console.WriteLine(strWarningMessage)

        RaiseEvent WarningEvent(strWarningMessage)
    End Sub

    Protected Sub ShowMessage(strMessage As String)
        If EchoMessagesToConsole Then Console.WriteLine(strMessage)
        RaiseEvent MessageEvent(strMessage)
    End Sub

    Private Sub UpdateStatus()
        mRecursiveCount += 1

        If mRecursiveCount <= mMaxRecursiveCount Then
            mPercentComplete = mRecursiveCount / CSng(mMaxRecursiveCount) * 100
        End If

    End Sub

    Private Sub ValidateBoundedSearchValues()
        For Each elementSymbol In mCandidateElements.Keys()
            Dim udtElementTolerances = mCandidateElements(elementSymbol)

            If udtElementTolerances.MinimumCount < 0 OrElse udtElementTolerances.MaximumCount > MAX_BOUNDED_SEARCH_COUNT Then
                If udtElementTolerances.MinimumCount < 0 Then udtElementTolerances.MinimumCount = 0
                If udtElementTolerances.MaximumCount > MAX_BOUNDED_SEARCH_COUNT Then udtElementTolerances.MaximumCount = MAX_BOUNDED_SEARCH_COUNT

                mCandidateElements(elementSymbol) = udtElementTolerances
            End If
        Next
    End Sub

    Private Sub ValidatePercentCompositionValues()
        For Each elementSymbol In mCandidateElements.Keys()
            Dim udtElementTolerances = mCandidateElements(elementSymbol)

            If udtElementTolerances.TargetPercentComposition < 0 Or udtElementTolerances.TargetPercentComposition > 100 Then
                If udtElementTolerances.TargetPercentComposition < 0 Then udtElementTolerances.TargetPercentComposition = 0
                If udtElementTolerances.TargetPercentComposition > 100 Then udtElementTolerances.TargetPercentComposition = 100

                mCandidateElements(elementSymbol) = udtElementTolerances
            End If
        Next
    End Sub

    Private Function ValidateSettings(calculationMode As eCalculationMode) As Boolean

        If mCandidateElements.Count = 0 Then
            ReportError("No candidate elements are defined; use method AddCandidateElement or property CandidateElements")
            Return False
        End If

        ValidateBoundedSearchValues()

        If calculationMode = eCalculationMode.MatchPercentComposition Then
            Dim totalTargetPercentComp = GetTotalPercentComposition()

            If Math.Abs(totalTargetPercentComp - 100) > Single.Epsilon Then
                ReportError("Sum of the target percentages must be 100%; it is currently " + totalTargetPercentComp.ToString("0.0") + "%")
                Return False
            End If

        End If

        Return True

    End Function

End Class