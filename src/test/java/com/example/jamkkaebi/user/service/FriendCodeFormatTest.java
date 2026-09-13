package com.example.jamkkaebi.user.service;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class FriendCodeFormatTest {

    @Test
    @DisplayName("하이픈·공백·소문자가 섞인 입력을 저장 형식으로 맞춘다")
    void normalizesHandTypedCode() {
        assertThat(FriendCodeFormat.normalize(" k7qm-3pvx ")).contains("K7QM3PVX");
    }

    @Test
    @DisplayName("길이가 다르거나 발급하지 않는 글자가 섞이면 형식 오류다")
    void rejectsMalformedCode() {
        assertThat(FriendCodeFormat.normalize("K7QM3PV")).isEmpty();
        assertThat(FriendCodeFormat.normalize("K7QM3PVO")).isEmpty(); // O 는 0 과 헷갈려 발급하지 않는다
        assertThat(FriendCodeFormat.normalize("K7QM3PV1")).isEmpty();
        assertThat(FriendCodeFormat.normalize(null)).isEmpty();
    }

    @Test
    @DisplayName("발급기가 만든 코드는 언제나 형식을 통과한다")
    void generatedCodesPassFormat() {
        FriendCodeGenerator generator = new FriendCodeGenerator();
        for (int i = 0; i < 1000; i++) {
            String code = generator.generate();
            assertThat(FriendCodeFormat.normalize(code)).contains(code);
        }
    }
}
