{
  "targets": [
    {
      "target_name": "cascode_native_addon",
      "sources": [
        "native/cascode_native_addon.c"
      ],
      "cflags": [
        "-std=c11"
      ],
      "xcode_settings": {
        "CLANG_CXX_LANGUAGE_STANDARD": "c++17"
      },
      "conditions": [
        [
          "OS==\"linux\" or OS==\"mac\"",
          {
            "libraries": [
              "-ldl"
            ]
          }
        ]
      ]
    }
  ]
}
