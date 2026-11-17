// Version comparison for the installer's downgrade warning (included in RawConverter.iss's [Code]). Same rules as
// the app's update check (UpdateChecker.IsNewer): "major.minor.patch[.build]" with an optional pre-release suffix
// ("1.3.0-nightly.20261114.6"), which sorts before the release itself; dot-separated pre-release parts compare as
// numbers when both are numeric, and a numeric part sorts before a text part; a leading "v" and a "+build" suffix
// are ignored. Written for Inno Setup's Pascal Script (also compiles with Free Pascal, for testing).

// Returns the text before the first Sep and removes it, and the Sep, from S.
function VersionNextPart(var S: String; Sep: Char): String;
var
  P: Integer;
begin
  P := Pos(Sep, S);
  if P = 0 then
  begin
    Result := S;
    S := '';
  end
  else
  begin
    Result := Copy(S, 1, P - 1);
    S := Copy(S, P + 1, Length(S));
  end;
end;

function VersionIsNumber(S: String): Boolean;
var
  I: Integer;
begin
  Result := Length(S) > 0;
  for I := 1 to Length(S) do
    if (S[I] < '0') or (S[I] > '9') then
      Result := False;
end;

function VersionSign(N: Integer): Integer;
begin
  if N > 0 then
    Result := 1
  else if N < 0 then
    Result := -1
  else
    Result := 0;
end;

// One dot-separated pre-release part.
function VersionCompareIdentifiers(A, B: String): Integer;
begin
  if VersionIsNumber(A) and VersionIsNumber(B) then
    Result := VersionSign(StrToIntDef(A, 0) - StrToIntDef(B, 0))
  else if VersionIsNumber(A) then
    Result := -1
  else if VersionIsNumber(B) then
    Result := 1
  else
    Result := VersionSign(CompareStr(A, B));
end;

// Removes a leading "v" and a "+build" suffix, and splits off the pre-release part (after the first "-").
procedure VersionSplit(var V: String; var Pre: String);
var
  P: Integer;
begin
  V := Trim(V);
  if (Length(V) > 0) and ((V[1] = 'v') or (V[1] = 'V')) then
    V := Copy(V, 2, Length(V));
  P := Pos('+', V);
  if P > 0 then
    V := Copy(V, 1, P - 1);
  Pre := '';
  P := Pos('-', V);
  if P > 0 then
  begin
    Pre := Copy(V, P + 1, Length(V));
    V := Copy(V, 1, P - 1);
  end;
end;

// 1 if A is newer than B, -1 if older, 0 if the same version.
function CompareVersions(A, B: String): Integer;
var
  APre, BPre: String;
  I, C: Integer;
begin
  VersionSplit(A, APre);
  VersionSplit(B, BPre);
  for I := 1 to 4 do
  begin
    C := VersionSign(StrToIntDef(VersionNextPart(A, '.'), 0) - StrToIntDef(VersionNextPart(B, '.'), 0));
    if C <> 0 then
    begin
      Result := C;
      Exit;
    end;
  end;
  if (APre = '') and (BPre = '') then
    Result := 0
  else if APre = '' then
    Result := 1
  else if BPre = '' then
    Result := -1
  else
  begin
    Result := 0;
    while (Result = 0) and ((APre <> '') or (BPre <> '')) do
    begin
      if APre = '' then
        Result := -1
      else if BPre = '' then
        Result := 1
      else
        Result := VersionCompareIdentifiers(VersionNextPart(APre, '.'), VersionNextPart(BPre, '.'));
    end;
  end;
end;
