package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

/**
 * 프로필 아바타(기본 또는 도깨비)
 *
 * <p>도깨비는 재봉인돼도 완료 상태가 내려가지 않으므로, 고른 아바타가 나중에 무효되지 않는다.
 */
@Service
public class AvatarService {

    private final UserRepository userRepository;
    private final UserRelicRepository userRelicRepository;
    private final RelicCatalog catalog;

    public AvatarService(UserRepository userRepository,
                         UserRelicRepository userRelicRepository,
                         RelicCatalog catalog) {
        this.userRepository = userRepository;
        this.userRelicRepository = userRelicRepository;
        this.catalog = catalog;
    }

    /**
     * 아바타를 바꾼다
     *
     * @param spiritId 도깨비 번호. {@code null} 이면 기본 아바타
     */
    @Transactional
    public User change(String friendCode, Integer spiritId) {
        User user = userRepository.findByFriendCode(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
        if (spiritId != null) {
            requireAwakened(user.getId(), spiritId);
        }
        user.updateSpirit(spiritId == null ? null : spiritId.longValue());
        return user;
    }

    private void requireAwakened(Long userId, Integer spiritId) {
        if (catalog.findRelic(spiritId).isEmpty()) {
            throw new BusinessException(ErrorCode.INVALID_INPUT,
                    List.of(new FieldError("spiritId", "존재하지 않는 도깨비입니다.")));
        }
        boolean awakened = userRelicRepository.findByUserIdAndRelicId(userId, spiritId)
                .filter(UserRelic::isCompleted)
                .isPresent();
        if (!awakened) {
            throw new BusinessException(ErrorCode.SPIRIT_NOT_AWAKENED);
        }
    }
}
