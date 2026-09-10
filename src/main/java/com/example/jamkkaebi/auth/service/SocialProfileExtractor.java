package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.springframework.stereotype.Component;

import java.util.Map;

/**
 * 제공자마다 다른 프로필 응답에서 필요한 값을 꺼낸다.
 *
 * <p>제공자별 응답 구조를 아는 <b>유일한 지점</b>이다. 제공자를 추가하면 여기에 분기를 더한다.
 */
@Component
public class SocialProfileExtractor {

    @SuppressWarnings("unchecked")
    public SocialProfile extract(AuthProvider provider, Map<String, Object> attributes) {
        return switch (provider) {
            // 구글은 openid 스코프라 OIDC 표준 클레임으로 온다. sub 가 회원번호다.
            case GOOGLE -> new SocialProfile(
                    requireText(attributes.get("sub")),
                    (String) attributes.get("name"));

            // 카카오는 회원번호가 최상위 id, 닉네임은 kakao_account.profile 안에 중첩돼 있다.
            // 닉네임 동의를 받지 못하면 그 중첩 구조 자체가 비어 오므로 빈 Map 으로 받아 넘긴다.
            case KAKAO -> {
                Map<String, Object> account =
                        (Map<String, Object>) attributes.getOrDefault("kakao_account", Map.of());
                Map<String, Object> profile =
                        (Map<String, Object>) account.getOrDefault("profile", Map.of());
                yield new SocialProfile(
                        requireText(attributes.get("id")),
                        (String) profile.get("nickname"));
            }
        };
    }

    /**
     * 회원번호가 없으면 로그인을 실패시킨다. 이 값이 계정 매칭 기준이라, 비어 있는 채로 진행하면
     * 모든 사용자가 한 계정으로 합쳐지거나 저장 단계에서 터진다.
     */
    private String requireText(Object value) {
        String text = value == null ? null : String.valueOf(value).trim();
        if (text == null || text.isEmpty()) {
            throw new BusinessException(ErrorCode.OAUTH_PROVIDER_ERROR);
        }
        return text;
    }
}
