UPDATE __SchemaMigrations
SET AppliedAtUtc =
        substr(AppliedAtUtc, 1, 10)
            || 'T'
            || substr(AppliedAtUtc, 12)
            || 'Z'
WHERE Version = 1
  AND substr(AppliedAtUtc, 11, 1) = ' ';