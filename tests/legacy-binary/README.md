# Legacy reference executable

The compiled legacy `hotkeySKG.exe` is kept in this repository only in encrypted form.

Expected file:

```text
tests/legacy-binary/hotkeySKG.exe.gpg
```

GitHub Actions decrypts it with the repository secret `LEGACY_EXE_GPG_PASSPHRASE` and then verifies the decrypted executable against the pinned SHA-256 before it can be used as a compatibility oracle.

Pinned plaintext SHA-256:

```text
5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea
```

Do not commit the passphrase. The encrypted file itself may be committed to the public repository.
