package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.HashMap;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 제공자 응답 매핑만 검증한다. (외부 호출 없음)
 */
class SocialProfileExtractorTest {

    private final SocialProfileExtractor extractor = new SocialProfileExtractor();

    @Test
    @DisplayName("구글: sub 를 회원번호로, name 을 닉네임으로 읽는다")
    void extractsGoogleClaims() {
        Map<String, Object> claims = new HashMap<>();
        claims.put("sub", "1234567890");
        claims.put("name", "잠깨비");

        SocialProfile profile = extractor.extract(AuthProvider.GOOGLE, claims);

        assertThat(profile.providerUserId()).isEqualTo("1234567890");
        assertThat(profile.rawNickname()).isEqualTo("잠깨비");
    }

    @Test
    @DisplayName("구글: 이메일·사진이 응답에 섞여 와도 읽지 않는다")
    void ignoresGoogleEmailAndPicture() {
        Map<String, Object> claims = new HashMap<>();
        claims.put("sub", "1234567890");
        claims.put("name", "잠깨비");
        claims.put("email", "player@example.com");
        claims.put("picture", "https://example.test/p.png");

        SocialProfile profile = extractor.extract(AuthProvider.GOOGLE, claims);

        // SocialProfile 에는 담을 자리 자체가 없다 — 수집하지 않겠다는 결정이 타입으로 강제된다.
        assertThat(profile).isEqualTo(new SocialProfile("1234567890", "잠깨비"));
    }

    @Test
    @DisplayName("카카오: 중첩된 kakao_account.profile 에서 닉네임을 꺼낸다")
    void extractsKakaoNestedNickname() {
        Map<String, Object> attributes = Map.of(
                "id", 987654321L,
                "kakao_account", Map.of(
                        "profile", Map.of("nickname", "잠깨비")));

        SocialProfile profile = extractor.extract(AuthProvider.KAKAO, attributes);

        assertThat(profile.providerUserId()).isEqualTo("987654321");
        assertThat(profile.rawNickname()).isEqualTo("잠깨비");
    }

    @Test
    @DisplayName("카카오: 닉네임 동의를 받지 못해 중첩 구조가 비어도 로그인은 계속된다")
    void survivesKakaoWithoutConsent() {
        SocialProfile profile = extractor.extract(AuthProvider.KAKAO, Map.of("id", 987654321L));

        assertThat(profile.providerUserId()).isEqualTo("987654321");
        assertThat(profile.rawNickname()).isNull();
    }

    @Test
    @DisplayName("회원번호가 없으면 로그인을 실패시킨다 — 빈 값으로 계정을 만들지 않는다")
    void rejectsMissingProviderUserId() {
        assertThatThrownBy(() -> extractor.extract(AuthProvider.GOOGLE, Map.of("name", "잠깨비")))
                .isInstanceOf(BusinessException.class)
                .extracting(e -> ((BusinessException) e).getErrorCode())
                .isEqualTo(ErrorCode.OAUTH_PROVIDER_ERROR);
    }
}
