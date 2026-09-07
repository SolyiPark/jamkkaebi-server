package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.common.policy.ForbiddenWordFilter;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class NicknamePolicyTest {

    private final NicknamePolicy policy = new NicknamePolicy(new ForbiddenWordFilter());

    @Test
    @DisplayName("10자를 넘는 소셜 닉네임을 잘라 저장 가능한 값으로 만든다")
    void truncatesLongNickname() {
        String sanitized = policy.sanitize("열두글자가넘어가는긴닉네임입니다");

        assertThat(sanitized).hasSize(10);
    }

    @Test
    @DisplayName("이모지가 섞여도 글자 단위로 자른다 — 깨진 글자를 만들지 않는다")
    void truncatesByCodePoint() {
        String sanitized = policy.sanitize("😀😀😀😀😀😀😀😀😀😀😀😀");

        assertThat(sanitized.codePointCount(0, sanitized.length())).isEqualTo(10);
        assertThat(Character.isHighSurrogate(sanitized.charAt(sanitized.length() - 1))).isFalse();
    }

    @Test
    @DisplayName("닉네임 동의를 받지 못해 null 이어도 가입을 막지 않는다")
    void fallsBackWhenMissing() {
        assertThat(policy.sanitize(null)).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
        assertThat(policy.sanitize("   ")).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
    }

    @Test
    @DisplayName("한 글자 이름은 기본값으로 보정한다")
    void correctsTooShortNickname() {
        assertThat(policy.sanitize("잠")).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
    }

    @Test
    @DisplayName("사이를 벌려 쓴 금칙어도 걸러낸다")
    void filtersSpacedForbiddenWord() {
        assertThat(policy.sanitize("관 리 자")).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
        assertThat(policy.sanitize("a-d-m-i-n")).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
    }

    @Test
    @DisplayName("정상 닉네임은 그대로 둔다")
    void keepsNormalNickname() {
        assertThat(policy.sanitize("  잠깨비  ")).isEqualTo("잠깨비");
    }
}
