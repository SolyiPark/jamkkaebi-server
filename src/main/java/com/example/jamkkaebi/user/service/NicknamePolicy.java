package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.common.policy.ForbiddenWordFilter;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.stereotype.Component;

/**
 * 소셜 닉네임을 잠깨비 닉네임으로 다듬는다. <b>절단 → 보정 → 금칙어 필터</b> 순서다.
 *
 * <p>이 단계가 없으면 <b>가입 자체가 실패한다</b> — 카카오·구글 닉네임은 10자를 넘을 수 있는데
 * {@code users.nickname} 은 10자이기 때문이다. 로그인은 게임의 유일한 진입로라 여기서 사용자를
 * 막으면 대안이 없다. 그래서 어떤 입력이 와도 <b>거부하지 않고 저장 가능한 값을 만들어</b> 낸다.
 * 마음에 들지 않는 이름은 가입 직후 수정 화면에서 바꾸게 한다.
 */
@Component
public class NicknamePolicy {

    /** 제공자가 닉네임을 주지 않았거나 쓸 수 없는 값일 때의 기본 이름. */
    public static final String DEFAULT_NICKNAME = "잠꾸러기";
    /** 최소 길이. 한 글자짜리 이름은 친구 목록에서 서로 구분되지 않는다. */
    public static final int MIN_LENGTH = 2;

    private final ForbiddenWordFilter forbiddenWordFilter;

    public NicknamePolicy(ForbiddenWordFilter forbiddenWordFilter) {
        this.forbiddenWordFilter = forbiddenWordFilter;
    }

    /**
     * 소셜에서 받은 이름을 저장 가능한 닉네임으로 바꾼다.
     *
     * @param rawNickname 제공자가 준 이름 ({@code null} 가능 — 동의를 받지 못한 경우)
     * @return 길이·금칙어 기준을 통과하는 닉네임. 절대 {@code null} 이 아니다.
     */
    public String sanitize(String rawNickname) {
        if (rawNickname == null) {
            return DEFAULT_NICKNAME;
        }
        String trimmed = rawNickname.trim();
        if (trimmed.isEmpty()) {
            return DEFAULT_NICKNAME;
        }

        String truncated = truncate(trimmed);
        if (truncated.codePointCount(0, truncated.length()) < MIN_LENGTH) {
            return DEFAULT_NICKNAME;
        }
        if (forbiddenWordFilter.containsForbiddenWord(truncated)) {
            return DEFAULT_NICKNAME;
        }
        return truncated;
    }

    /**
     * 최대 길이로 자른다.
     *
     * <p><b>코드포인트</b> 기준으로 센다.
     */
    private String truncate(String nickname) {
        int codePoints = nickname.codePointCount(0, nickname.length());
        if (codePoints <= User.NICKNAME_MAX_LENGTH) {
            return nickname;
        }
        return nickname.substring(0, nickname.offsetByCodePoints(0, User.NICKNAME_MAX_LENGTH));
    }
}
