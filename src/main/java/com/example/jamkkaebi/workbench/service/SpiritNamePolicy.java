package com.example.jamkkaebi.workbench.service;

import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.common.policy.ForbiddenWordFilter;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import org.springframework.stereotype.Component;

import java.util.List;

/**
 * 도깨비 이름 검사. <b>친구에게 노출되므로</b> 닉네임과 같은 금칙어 필터를 건다
 *
 * <p>닉네임과 달리 여기서는 조용히 기본값으로 바꾸지 않고 <b>거절한다</b> — 소셜 닉네임은 사용자가
 * 고른 값이 아니라 막으면 가입할 길이 없지만, 이 이름은 사용자가 방금 친 값이라 수정 가능하다
 *
 * @see com.example.jamkkaebi.user.service.NicknamePolicy
 */
@Component
public class SpiritNamePolicy {

    private final ForbiddenWordFilter forbiddenWordFilter;

    public SpiritNamePolicy(ForbiddenWordFilter forbiddenWordFilter) {
        this.forbiddenWordFilter = forbiddenWordFilter;
    }

    /**
     * @param name 사용자가 입력한 이름. {@code null} 이나 공백이면 기본 이름으로 되돌린다는 뜻이다
     * @return 저장할 이름. 기본 이름으로 되돌릴 때는 {@code null}
     */
    public String validate(String name) {
        if (name == null) {
            return null;
        }
        String trimmed = name.trim();
        if (trimmed.isEmpty()) {
            return null;
        }

        int codePoints = trimmed.codePointCount(0, trimmed.length());
        if (codePoints < UserArtifact.SPIRIT_NAME_MIN_LENGTH) {
            throw reject(UserArtifact.SPIRIT_NAME_MIN_LENGTH + "자 이상이어야 합니다.");
        }
        if (codePoints > UserArtifact.SPIRIT_NAME_MAX_LENGTH) {
            throw reject(UserArtifact.SPIRIT_NAME_MAX_LENGTH + "자를 넘을 수 없습니다.");
        }
        if (forbiddenWordFilter.containsForbiddenWord(trimmed)) {
            throw reject("사용할 수 없는 단어가 들어 있습니다.");
        }
        return trimmed;
    }

    private BusinessException reject(String reason) {
        return new BusinessException(ErrorCode.INVALID_INPUT, List.of(new FieldError("name", reason)));
    }
}
