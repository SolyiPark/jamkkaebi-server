package com.example.jamkkaebi.user.domain;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 잠깨비 플레이어 계정.
 *
 * <p><b>식별자가 세 층으로 나뉜다.</b>
 * <ul>
 *   <li>{@code provider + providerUserId} — 소셜이 이 값만 돌려주므로 계정 매칭 기준. 변경 불가.</li>
 *   <li>{@code id} — 내부 PK. 순번이라 노출하면 가입 수·가입 순서가 새므로 <b>내부 전용</b>.</li>
 *   <li>{@code friendCode} — API 응답과 JWT {@code sub} 에 나가는 <b>유일한 외부 식별자</b>.</li>
 * </ul>
 *
 * <p><b>이메일과 프로필 이미지는 수집하지 않는다.</b> 이메일 컬럼은 나중을 위해 자리만 두고 항상
 * {@code null} 이며, 프로필은 소셜 사진이 아니라 사전 제작 아바타({@code spiritId})에서 고른다.
 */
@Entity
@Table(
        name = "users",
        uniqueConstraints = {
                @UniqueConstraint(
                        name = "uk_users_provider_user",
                        columnNames = {"provider", "provider_user_id"}),
                @UniqueConstraint(name = "uk_users_friend_code", columnNames = "friend_code")
        }
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class User extends BaseTimeEntity {

    /** 친구 코드 길이. {@code CHAR(8)} 고정이며 재발급하지 않는다. */
    public static final int FRIEND_CODE_LENGTH = 8;
    /** 닉네임 최대 길이(코드포인트). 소셜 닉네임이 더 길면 잘라서 저장한다. */
    public static final int NICKNAME_MAX_LENGTH = 10;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 30)
    private AuthProvider provider;

    /** 소셜 서비스가 부여한 고유 회원번호. 외부로 내보내지 않는다. */
    @Column(name = "provider_user_id", nullable = false, length = 100)
    private String providerUserId;

    /** 밖으로 나가는 유일한 식별자. 가입 시 무작위 발급하며 재발급하지 않는다. */
    @Column(name = "friend_code", nullable = false, columnDefinition = "CHAR(8)")
    private String friendCode;

    /** 중복을 허용한다 — 사람을 구분하는 것은 친구 코드이지 닉네임이 아니다. */
    @Column(nullable = false, length = NICKNAME_MAX_LENGTH)
    private String nickname;

    /**
     * 수집하지 않는다. 항상 {@code null} 이며, 나중에 필요해질 때를 위해 컬럼만 둔다.
     */
    @Column(length = 255)
    private String email;

    /** 프로필 아바타(정령) 식별자. {@code null} 이면 기본 사람 모양이다. */
    @Column(name = "spirit_id")
    private Long spiritId;

    @Builder
    private User(AuthProvider provider, String providerUserId, String friendCode, String nickname) {
        this.provider = provider;
        this.providerUserId = providerUserId;
        this.friendCode = friendCode;
        this.nickname = nickname;
    }

    /** 닉네임을 변경한다. 가입 직후 수정 화면에서 부르는 것을 전제로 한다. */
    public void updateNickname(String nickname) {
        this.nickname = nickname;
    }

    /** 프로필 아바타를 바꾼다. */
    public void updateSpirit(Long spiritId) {
        this.spiritId = spiritId;
    }
}
